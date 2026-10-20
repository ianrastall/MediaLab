using System.IO;
using System.Globalization;
using Lab.Core.Processes;

namespace VideoLab.Core.Media;

public enum VideoRenderOutcome
{
    Success,
    ValidationFailed,
    Canceled,
    FfmpegFailed,
}

/// <summary>Progress update during a render.</summary>
public readonly record struct VideoRenderProgress(double Percent, TimeSpan Position, TimeSpan Total);

/// <summary>Final result of a render.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="OutputPath">The promoted final file on success; otherwise null.</param>
/// <param name="Error">Human-readable failure detail when not successful.</param>
public sealed record VideoRenderResult(VideoRenderOutcome Outcome, string? OutputPath, string? Error)
{
    public bool Success => Outcome == VideoRenderOutcome.Success;
}

/// <summary>
/// Runs a single VideoLab render end-to-end: validate inputs, probe duration/dimensions,
/// build the filter graph, render to a <c>.partial.mp4</c>, parse progress, and promote to the
/// final path only on success (deleting the partial on failure or cancellation).
/// </summary>
public sealed class VideoRenderService
{
    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;
    private readonly FfprobeService _ffprobe;
    private readonly ImageProbeService _imageProbe;

    public VideoRenderService(
        IProcessRunner runner,
        string ffmpegPath,
        FfprobeService ffprobe,
        ImageProbeService imageProbe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _ffprobe = ffprobe;
        _imageProbe = imageProbe;
    }

    public async Task<VideoRenderResult> RenderAsync(
        VideoRenderJob job,
        IProgress<VideoRenderProgress>? progress = null,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        // 1-2. Inputs exist.
        if (!File.Exists(job.AudioPath))
        {
            return Fail(VideoRenderOutcome.ValidationFailed, $"Audio file not found: {job.AudioPath}");
        }

        if (!File.Exists(job.ImagePath))
        {
            return Fail(VideoRenderOutcome.ValidationFailed, $"Image file not found: {job.ImagePath}");
        }

        // 3. Image must be exactly the target size (no hidden scaling in v1).
        var dims = await _imageProbe.GetDimensionsAsync(job.ImagePath, cancellationToken).ConfigureAwait(false);
        if (dims is null)
        {
            return Fail(VideoRenderOutcome.ValidationFailed, $"Could not read image dimensions: {job.ImagePath}");
        }

        if (dims.Value.Width != job.Width || dims.Value.Height != job.Height)
        {
            return Fail(VideoRenderOutcome.ValidationFailed,
                $"Image must be exactly {job.Width}x{job.Height}, but is {dims.Value}.");
        }

        // 4. Duration drives the progress percentage.
        var duration = await _ffprobe.GetDurationAsync(job.AudioPath, cancellationToken).ConfigureAwait(false);
        var total = duration ?? TimeSpan.Zero;

        // 5. Build the graph.
        string graph;
        try
        {
            graph = SpectrumBarFilterBuilder.Build(job);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Fail(VideoRenderOutcome.ValidationFailed, ex.Message);
        }

        // 6. Ensure the output directory exists (the user may have typed a new path); the
        //    .partial is written here too, so create it before running ffmpeg, not just before move.
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(job.OutputPath));
        if (!string.IsNullOrEmpty(outputDir))
        {
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(VideoRenderOutcome.ValidationFailed, $"Cannot create output directory: {ex.Message}");
            }
        }

        // Clear any stale partial.
        var partialPath = OutputPathBuilder.Partial(job.OutputPath);
        TryDelete(partialPath);

        // 7-10. Run ffmpeg. The try/finally guarantees partial cleanup even if RunAsync throws
        //       (e.g. process fails to start, or KillTree throws on an unusual platform).
        var args = BuildArguments(job, graph, partialPath, duration);
        var parser = new FfmpegProgressParser();
        ProcessResult result;

        try
        {
            result = await _runner.RunAsync(
                _ffmpegPath,
                args,
                onOutputLine: line =>
                {
                    if (parser.TryConsume(line, out var p))
                    {
                        progress?.Report(new VideoRenderProgress(p.PercentOf(total), p.OutTime ?? TimeSpan.Zero, total));
                    }
                },
                onErrorLine: onLog,
                captureOutput: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }

        if (result.Canceled)
        {
            TryDelete(partialPath);
            return Fail(VideoRenderOutcome.Canceled, "Render canceled.");
        }

        if (!result.Success || !File.Exists(partialPath))
        {
            TryDelete(partialPath);
            return Fail(VideoRenderOutcome.FfmpegFailed, $"ffmpeg exited with code {result.ExitCode}.");
        }

        // 11. Promote.
        try
        {
            File.Move(partialPath, job.OutputPath, overwrite: true);
        }
        catch (IOException ex)
        {
            TryDelete(partialPath);
            return Fail(VideoRenderOutcome.FfmpegFailed, $"Could not finalize output: {ex.Message}");
        }

        return new VideoRenderResult(VideoRenderOutcome.Success, job.OutputPath, null);
    }

    /// <summary>The exact ffmpeg argument list, in order. Exposed for testing/inspection.</summary>
    /// <param name="duration">
    /// Probed audio duration. When provided and non-zero, used as an explicit <c>-t</c> output
    /// limit instead of <c>-shortest</c>. <c>-shortest</c> has a documented misbehaviour with
    /// complex filtergraphs and looped image inputs: the <c>showfreqs</c> window buffer (~170 ms
    /// at the current settings) can cause EOF timestamps in the video path to race ahead of the
    /// true audio end, silently dropping the final fraction of a second of audio. An explicit
    /// <c>-t</c> from a pre-probed duration is deterministic and immune to this race.
    /// Falls back to <c>-shortest</c> only when no duration is available (should not happen for
    /// a valid audio file, but prevents an infinite encode if it somehow does).
    /// </param>
    public static IReadOnlyList<string> BuildArguments(
        VideoRenderJob job,
        string filterGraph,
        string partialOutputPath,
        TimeSpan? duration = null)
    {
        var c = CultureInfo.InvariantCulture;
        var args = new List<string>
        {
            "-y", "-hide_banner", "-nostdin",
            "-loop", "1",
            "-framerate", job.FrameRate.ToString(c),
            "-i", job.ImagePath,
            "-i", job.AudioPath,
            "-filter_complex", filterGraph,
            "-map", "[v]",
            "-map", "[aout]",
        };

        // Prefer an explicit output duration over -shortest (see param docs above).
        if (duration is { } d && d > TimeSpan.Zero)
        {
            args.AddRange(["-t", d.TotalSeconds.ToString("F6", c)]);
        }
        else
        {
            args.Add("-shortest");
        }

        args.AddRange([
            "-r", job.FrameRate.ToString(c),
            "-c:v", "h264_nvenc",
            "-preset", "p6",
            "-tune", "hq",
            "-rc", "vbr",
            "-cq", "19",
            "-b:v", "0",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "384k",
            "-ar", "96000",                   // deliver at 384 kb/s and 96 kHz regardless of source
            "-max_muxing_queue_size", "9999", // default 128 can fill when A/V timestamps diverge during NVENC encode
            "-movflags", "+faststart",
            "-progress", "pipe:1",            // machine-readable progress on stdout
            "-stats_period", "0.5",           // governs -progress update cadence; 0.5 s is the default
            "-nostats",                       // suppress only the human-readable stderr stats line
            partialOutputPath,
        ]);

        return args;
    }

    private static VideoRenderResult Fail(VideoRenderOutcome outcome, string error) => new(outcome, null, error);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort; a locked partial will be overwritten on the next run via -y.
        }
    }
}
