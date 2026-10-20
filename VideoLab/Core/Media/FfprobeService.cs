using System.Globalization;
using Lab.Core.Processes;

namespace VideoLab.Core.Media;

/// <summary>
/// Probes media with ffprobe. Parsing is separated from the process invocation so the
/// numeric handling (invariant culture, "N/A") is unit-testable without a real ffprobe.
/// </summary>
public sealed class FfprobeService
{
    private readonly ProcessRunner _runner;
    private readonly string _ffprobePath;

    public FfprobeService(ProcessRunner runner, string ffprobePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        _runner = runner;
        _ffprobePath = ffprobePath;
    }

    /// <summary>
    /// Returns the container duration of <paramref name="mediaPath"/>, or null if ffprobe
    /// failed or reported no usable duration.
    /// </summary>
    public async Task<TimeSpan?> GetDurationAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);

        // -of default=nokey=1:noprint_wrappers=1 prints just the bare value, e.g. "212.345000".
        string[] args =
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=nokey=1:noprint_wrappers=1",
            mediaPath,
        ];

        var result = await _runner.RunAsync(_ffprobePath, args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return null;
        }

        return TryParseDurationSeconds(result.StandardOutput, out var duration) ? duration : null;
    }

    /// <summary>
    /// Parses an ffprobe duration value (bare seconds, invariant culture). Returns false for
    /// empty output or the literal <c>N/A</c> ffprobe emits when no duration is available.
    /// </summary>
    public static bool TryParseDurationSeconds(string? ffprobeOutput, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(ffprobeOutput))
        {
            return false;
        }

        var text = ffprobeOutput.Trim();
        if (text.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
            && !double.IsInfinity(seconds))
        {
            duration = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return false;
    }
}
