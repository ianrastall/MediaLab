using VideoLab.Core.Media;
using Lab.UI.Controls;
using Lab.Core.Processes;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Media;

/// <summary>
/// Drives the real bundled ffprobe against a real generated media file. Self-skips (passes
/// trivially) when ffmpeg/ffprobe are not installed on the machine.
/// </summary>
[Trait("Category", "Integration")]
public class FfprobeServiceIntegrationTests
{
    [Fact]
    public async Task GetDurationAsync_ReturnsTrueDuration_OfGeneratedFile()
    {
        var ffmpeg = RealTools.Ffmpeg;
        var ffprobe = RealTools.Ffprobe;
        if (ffmpeg is null || ffprobe is null)
        {
            return; // tools unavailable on this machine; nothing to assert
        }

        using var temp = new TempDir();
        var wav = temp.Combine("tone.wav");

        var runner = new ProcessRunner();

        // Generate exactly 2 seconds of audio with the real ffmpeg.
        var gen = await runner.RunAsync(ffmpeg,
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
            wav,
        ]);
        Assert.True(gen.Success, $"ffmpeg generation failed: {gen.StandardError}");

        var probe = new FfprobeService(runner, ffprobe);
        var duration = await probe.GetDurationAsync(wav);

        Assert.NotNull(duration);
        Assert.Equal(2.0, duration!.Value.TotalSeconds, precision: 1);
    }

    [Fact]
    public async Task GetDurationAsync_ReturnsNull_ForMissingFile()
    {
        var ffprobe = RealTools.Ffprobe;
        if (ffprobe is null)
        {
            return;
        }

        var probe = new FfprobeService(new ProcessRunner(), ffprobe);
        var duration = await probe.GetDurationAsync(@"C:\definitely\not\here\nope.wav");

        Assert.Null(duration);
    }
}
