using System.IO;
using VideoLab.Core.Media;
using Lab.UI.Controls;
using Lab.Core.Processes;
using MediaLab.Tests.TestSupport;

namespace MediaLab.Tests.Media;

/// <summary>
/// Drives the real VideoRenderService through ffmpeg/ffprobe, rendering a genuine (short) 4K
/// clip with the real filter graph. Self-skips when tools are unavailable.
/// </summary>
[Trait("Category", "Integration")]
public class VideoRenderServiceIntegrationTests
{
    private static (VideoRenderService svc, ProcessRunner runner)? Build()
    {
        var ffmpeg = RealTools.Ffmpeg;
        var ffprobe = RealTools.Ffprobe;
        if (ffmpeg is null || ffprobe is null)
        {
            return null;
        }

        var runner = new ProcessRunner();
        var svc = new VideoRenderService(runner, ffmpeg, new FfprobeService(runner, ffprobe), new ImageProbeService(runner, ffprobe));
        return (svc, runner);
    }

    private static async Task GenAsync(ProcessRunner runner, string ffmpeg, IEnumerable<string> args)
    {
        var r = await runner.RunAsync(ffmpeg, args);
        Assert.True(r.Success, $"ffmpeg setup failed: {r.StandardError}");
    }

    [Fact]
    public async Task RenderAsync_ProducesPromoted4kMp4_AndReportsProgress()
    {
        if (Build() is not { } b)
        {
            return;
        }

        var ffmpeg = RealTools.Ffmpeg!;
        using var temp = new TempDir();
        var image = temp.Combine("cover.png");
        var audio = temp.Combine("song.wav");
        var output = temp.Combine("song_video.mp4");

        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=navy:s=3840x2160", "-frames:v", "1", image]);
        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=1", "-ac", "2", audio]);

        var job = VideoRenderJob.Create(audio, image, output, "#C01822");
        var progress = new ListProgress<VideoRenderProgress>();

        var result = await b.svc.RenderAsync(job, progress);

        Assert.True(result.Success, result.Error);
        Assert.Equal(output, result.OutputPath);
        Assert.True(File.Exists(output), "final output missing");
        Assert.False(File.Exists(OutputPathBuilder.Partial(output)), "partial should have been promoted/removed");
        Assert.NotEmpty(progress.Items);

        // The promoted file is a real 4K H.264 / AAC MP4.
        var probe = new ImageProbeService(b.runner, RealTools.Ffprobe!);
        var dims = await probe.GetDimensionsAsync(output);
        Assert.Equal(new ImageDimensions(3840, 2160), dims);
    }

    [Fact]
    public async Task RenderAsync_CreatesMissingOutputDirectory()
    {
        if (Build() is not { } b)
        {
            return;
        }

        var ffmpeg = RealTools.Ffmpeg!;
        using var temp = new TempDir();
        var image = temp.Combine("cover.png");
        var audio = temp.Combine("song.wav");
        // Output lives under nested directories that do NOT exist yet.
        var output = temp.Combine(@"new\nested\dir\song_video.mp4");

        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=navy:s=3840x2160", "-frames:v", "1", image]);
        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=1", "-ac", "2", audio]);

        var job = VideoRenderJob.Create(audio, image, output, "#C01822");
        var result = await b.svc.RenderAsync(job);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(output), "output should exist in the freshly-created directory");
    }

    [Fact]
    public async Task RenderAsync_RejectsWrongSizedImage()
    {
        if (Build() is not { } b)
        {
            return;
        }

        var ffmpeg = RealTools.Ffmpeg!;
        using var temp = new TempDir();
        var image = temp.Combine("small.png");
        var audio = temp.Combine("song.wav");

        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=red:s=1280x720", "-frames:v", "1", image]);
        await GenAsync(b.runner, ffmpeg, ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=220:duration=1", audio]);

        var job = VideoRenderJob.Create(audio, image, temp.Combine("out.mp4"), "#C01822");
        var result = await b.svc.RenderAsync(job);

        Assert.Equal(VideoRenderOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("3840x2160", result.Error);
    }

    [Fact]
    public async Task RenderAsync_MissingAudio_ValidationFails_WithoutTools()
    {
        // No tool dependency: validation short-circuits before any probe.
        var runner = new ProcessRunner();
        var svc = new VideoRenderService(runner, "ffmpeg.exe", new FfprobeService(runner, "ffprobe.exe"), new ImageProbeService(runner, "ffprobe.exe"));
        var job = VideoRenderJob.Create(@"C:\nope\missing.wav", @"C:\nope\missing.png", @"C:\nope\out.mp4", "#C01822");

        var result = await svc.RenderAsync(job);

        Assert.Equal(VideoRenderOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Audio file not found", result.Error);
    }
}
