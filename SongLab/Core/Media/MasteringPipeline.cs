using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lab.Core.Processes;
using Lab.Core.Tools;

namespace SongLab.Core.Media;

public sealed class MasteringPipeline
{
    private readonly ToolLocator _tools;
    private readonly IProcessRunner _runner;

    public MasteringPipeline(ToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

    public async Task RunMasteringAsync(
        string sourcePath,
        string outputPath,
        DeliveryFormat format,
        double tempoPercent,
        double highPassHz,
        double lowPassHz,
        double bassDb,
        double trebleDb,
        bool applyLoudness,
        double loudnessTargetLufs,
        Action<string>? onStatus,
        CancellationToken cancellationToken = default)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "SongLab_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var ffmpeg = LocateTool("ffmpeg");
            var sox = LocateTool("sox");
            var rubberband = LocateTool("rubberband-r3");
            var rsgain = LocateTool("rsgain");

            // Step 1: Decode to 24-bit 48kHz WAV
            onStatus?.Invoke("1/5: Decoding source...");
            var step1Wav = Path.Combine(tempDir, "step1_decode.wav");
            await RunOrThrowAsync(ffmpeg, ["-y", "-i", sourcePath, "-acodec", "pcm_s24le", "-ar", "48000", step1Wav], onStatus, cancellationToken);

            // Step 2: SoX DSP
            onStatus?.Invoke("2/5: Applying SoX DSP filters...");
            var step2Wav = Path.Combine(tempDir, "step2_sox.wav");
            var soxArgs = new List<string> { step1Wav, step2Wav };
            
            // Normalize audio before EQ to prevent clipping during boosts? SoX has "norm" but it's fine for now to just pass options.
            // Wait, sox syntax is: sox in out [effect [options]] ...
            if (highPassHz > 0) { soxArgs.Add("highpass"); soxArgs.Add(highPassHz.ToString("0.#")); }
            if (lowPassHz > 0) { soxArgs.Add("lowpass"); soxArgs.Add(lowPassHz.ToString("0.#")); }
            if (bassDb != 0) { soxArgs.Add("bass"); soxArgs.Add(bassDb.ToString("0.#")); }
            if (trebleDb != 0) { soxArgs.Add("treble"); soxArgs.Add(trebleDb.ToString("0.#")); }
            
            if (soxArgs.Count > 2)
            {
                await RunOrThrowAsync(sox, soxArgs, onStatus, cancellationToken);
            }
            else
            {
                // No filters needed, just copy
                File.Copy(step1Wav, step2Wav, true);
            }

            // Step 3: Rubber Band Time Stretch
            var step3Wav = Path.Combine(tempDir, "step3_rb.wav");
            if (Math.Abs(tempoPercent - 100) > 0.01)
            {
                onStatus?.Invoke("3/5: Time-stretching with Rubber Band R3...");
                // rubberband-r3 uses --tempo for ratio. Tempo of 1.05 = 105%.
                double tempoRatio = tempoPercent / 100.0;
                await RunOrThrowAsync(rubberband, ["--tempo", tempoRatio.ToString("0.####"), "--fine", "--ignore-clipping", step2Wav, step3Wav], onStatus, cancellationToken);
            }
            else
            {
                onStatus?.Invoke("3/5: Skipping time-stretch (Tempo is 100%)...");
                File.Copy(step2Wav, step3Wav, true);
            }

            // Step 4: Encode
            onStatus?.Invoke($"4/5: Encoding to {format}...");
            if (File.Exists(outputPath)) File.Delete(outputPath);
            
            var encodeArgs = new List<string> { "-y", "-i", step3Wav };
            if (format == DeliveryFormat.Flac) { encodeArgs.Add("-c:a"); encodeArgs.Add("flac"); encodeArgs.Add("-compression_level"); encodeArgs.Add("8"); }
            else if (format == DeliveryFormat.Aiff) { encodeArgs.Add("-c:a"); encodeArgs.Add("pcm_s24be"); }
            else { encodeArgs.Add("-c:a"); encodeArgs.Add("pcm_s24le"); } // Default WAV
            
            encodeArgs.Add(outputPath);
            await RunOrThrowAsync(ffmpeg, encodeArgs, onStatus, cancellationToken);

            // Step 5: Loudness (rsgain)
            if (applyLoudness)
            {
                onStatus?.Invoke("5/5: Applying loudness tags (rsgain)...");
                // rsgain custom -s i -l -14 -c p output.flac
                var rsgainResult = await _runner.RunAsync(rsgain, ["custom", "-s", "i", "-l", loudnessTargetLufs.ToString("0.#"), "-c", "p", outputPath], onOutputLine: onStatus, onErrorLine: onStatus, cancellationToken: cancellationToken);
                if (!rsgainResult.Success)
                {
                    onStatus?.Invoke($"⚠ rsgain failed (exit {rsgainResult.ExitCode}) — output is still valid, just untagged.");
                }
            }
            else
            {
                onStatus?.Invoke("5/5: Skipping loudness normalization.");
            }

            onStatus?.Invoke("Mastering complete.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* Ignore cleanup errors */ }
        }
    }

    private string LocateTool(string tool)
    {
        return _tools.Locate(tool) ?? throw new InvalidOperationException($"Required tool '{tool}' was not found.");
    }

    private async Task RunOrThrowAsync(string toolPath, IEnumerable<string> args, Action<string>? onLog, CancellationToken ct)
    {
        var result = await _runner.RunAsync(toolPath, args, onOutputLine: onLog, onErrorLine: onLog, cancellationToken: ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Tool '{Path.GetFileName(toolPath)}' failed with exit code {result.ExitCode}.\nError: {result.StandardError}");
        }
    }
}
