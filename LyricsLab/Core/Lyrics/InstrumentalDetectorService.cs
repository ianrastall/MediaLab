using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Lab.Core.Processes;
using Lab.Core.Tools;

namespace LyricsLab.Core.Lyrics;

public sealed record InstrumentalGap(TimeSpan Start, TimeSpan End, TimeSpan Duration);

public class InstrumentalDetectorService
{
    private readonly ToolLocator _tools;
    private readonly IProcessRunner _runner;

    public InstrumentalDetectorService(ToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

    private class VadOutput
    {
        [JsonPropertyName("gaps")]
        public List<VadGap>? Gaps { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private class VadGap
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }
    }

    public async Task<IReadOnlyList<InstrumentalGap>> DetectInstrumentalGapsAsync(
        string audioPath, Action<string>? onLog = null, CancellationToken ct = default)
    {
        // We use the whisperx virtual environment python
        var pythonPath = LyricsLab.Core.Lyrics.WhisperXTranscriberService.VenvPython(LyricsLab.Core.Lyrics.WhisperInstallerService.ResolveWhisperXInstallDir());
        if (!System.IO.File.Exists(pythonPath))
        {
            throw new InvalidOperationException("WhisperX python environment not found. Please run Setup Dependencies.");
        }

        // The python script should be alongside the application or in a Scripts folder
        var scriptDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        var scriptPath = System.IO.Path.Combine(scriptDir, "vad_detect.py");
        if (!System.IO.File.Exists(scriptPath))
        {
            // Fallback for development if not copied to bin/Scripts
            var devPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LyricsLab", "Core", "Scripts", "vad_detect.py");
            
            if (System.IO.File.Exists(devPath))
            {
                scriptPath = System.IO.Path.GetFullPath(devPath);
            }
            else
            {
                throw new System.IO.FileNotFoundException($"Could not find vad_detect.py at {scriptPath} or {devPath}");
            }
        }

        onLog?.Invoke("[vad] Running Voice Activity Detection...");

        var outputBuilder = new System.Text.StringBuilder();
        Action<string> onOutput = line =>
        {
            outputBuilder.AppendLine(line);
        };

        var result = await _runner.RunAsync(
            pythonPath,
            [scriptPath, audioPath],
            workingDirectory: null,
            onOutputLine: onOutput,
            onErrorLine: onLog,
            captureOutput: true,
            cancellationToken: ct);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"VAD detection failed with exit code {result.ExitCode}.");
        }

        var jsonOutput = outputBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(jsonOutput))
        {
            throw new InvalidOperationException("VAD script returned empty output.");
        }

        try
        {
            // Find the first '{' in case there was any stray print before the JSON
            var startIdx = jsonOutput.IndexOf('{');
            if (startIdx >= 0)
            {
                jsonOutput = jsonOutput.Substring(startIdx);
            }

            var output = JsonSerializer.Deserialize<VadOutput>(jsonOutput);
            if (output?.Error != null)
            {
                throw new InvalidOperationException($"VAD script error: {output.Error}");
            }

            var gaps = new List<InstrumentalGap>();
            if (output?.Gaps != null)
            {
                foreach (var gap in output.Gaps)
                {
                    gaps.Add(new InstrumentalGap(
                        TimeSpan.FromSeconds(gap.Start),
                        TimeSpan.FromSeconds(gap.End),
                        TimeSpan.FromSeconds(gap.Duration)));
                }
            }

            return gaps;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse VAD output: {ex.Message}\nRaw output: {jsonOutput}");
        }
    }
}
