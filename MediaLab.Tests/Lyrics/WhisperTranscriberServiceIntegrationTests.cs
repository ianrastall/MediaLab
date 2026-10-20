using System.IO;
using LyricsLab.Core.Lyrics;
using Lab.Core.Processes;
using Lab.Core.Tools;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Lyrics;

/// <summary>
/// Drives the real ffmpeg + whisper-cli binaries. Self-skips when either tool is absent.
/// The test uses a short synthesized WAV so no real audio asset is needed in the repo.
/// </summary>
[Trait("Category", "Integration")]
public class WhisperTranscriberServiceIntegrationTests
{
    [Fact]
    public async Task TranscribeAsync_ShortSineWave_CompletesWithoutError()
    {
        var ffmpeg = RealTools.Ffmpeg;
        var whisperCli = RealTools.WhisperCli;
        var model = RealTools.WhisperModel;

        if (ffmpeg is null || whisperCli is null || model is null)
        {
            // Tools not installed on this machine — skip gracefully.
            return;
        }

        // Generate a 3-second 440 Hz sine wave WAV via ffmpeg.
        using var temp = new TempDir();
        var sineWav = temp.Combine("sine.wav");

        var runner = new ProcessRunner();
        var genResult = await runner.RunAsync(
            ffmpeg,
            ["-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-ar", "16000", "-ac", "1", sineWav, "-y"],
            captureOutput: false);

        Assert.True(genResult.Success, $"ffmpeg sine generation failed: {genResult.StandardError}");
        Assert.True(File.Exists(sineWav), "sine WAV not created");

        var svc = new WhisperTranscriberService(new ToolLocator());
        var logs = new List<string>();

        // Transcribe — a pure tone will likely return zero or nonsense words but must not throw.
        var words = await svc.TranscribeAsync(sineWav, model, onLogLine: logs.Add);

        Assert.NotNull(words);
        Assert.Contains(logs, l => l.Contains("[whisper]"));
    }

    [Fact]
    public void FindModel_WhenWhisperInstalled_ReturnsAbsolutePath()
    {
        if (RealTools.WhisperCli is null)
        {
            return;
        }

        var svc = new WhisperTranscriberService(new ToolLocator());
        var model = svc.FindModel();

        Assert.NotNull(model);
        Assert.True(File.Exists(model), $"Model path does not exist: {model}");
        Assert.EndsWith(".bin", model, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscribeAsync_MissingAudioFile_Throws()
    {
        if (RealTools.WhisperCli is null || RealTools.WhisperModel is null)
        {
            return;
        }

        var svc = new WhisperTranscriberService(new ToolLocator());
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => svc.TranscribeAsync(@"C:\nonexistent_audio_9f3a.wav"));
    }
}
