using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class VideoRenderJobTests
{
    [Fact]
    public void Create_AppliesSuiteDefaults()
    {
        var job = VideoRenderJob.Create("song.wav", "cover.png", "out.mp4", "#C01822");

        Assert.Equal(3840, job.Width);
        Assert.Equal(2160, job.Height);
        Assert.Equal(60, job.FrameRate);
        Assert.Equal(40, job.BarCount);
        Assert.Equal(0.35, job.Opacity);
    }

    [Fact]
    public void Create_NormalizesBarColorAndDerivesPeak()
    {
        var job = VideoRenderJob.Create("a", "b", "c", "c01822");

        Assert.Equal("#C01822", job.BarHex);     // normalized to canonical upper hex
        Assert.Equal("#861118", job.PeakHex);    // 30% darker
    }

    [Fact]
    public void BuildArguments_AreInExpectedOrderWithProgressAndCodecs()
    {
        var job = VideoRenderJob.Create("song.wav", "cover.png", "out.mp4", "#C01822");
        var args = VideoRenderService.BuildArguments(job, "GRAPH", "out.partial.mp4").ToList();

        // Inputs: image looped first, then audio.
        var iFlags = args.Select((a, idx) => (a, idx)).Where(t => t.a == "-i").Select(t => t.idx).ToList();
        Assert.Equal(2, iFlags.Count);
        Assert.Equal("cover.png", args[iFlags[0] + 1]);
        Assert.Equal("song.wav", args[iFlags[1] + 1]);

        // Codecs and the requested audio delivery settings.
        Assert.Contains("h264_nvenc", args);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("19", args[args.IndexOf("-cq") + 1]);
        Assert.Equal("384k", args[args.IndexOf("-b:a") + 1]);
        Assert.Equal("96000", args[args.IndexOf("-ar") + 1]);

        // Machine-readable progress on stdout, and output is the .partial file.
        Assert.Equal("pipe:1", args[args.IndexOf("-progress") + 1]);
        Assert.Equal("out.partial.mp4", args[^1]);
        Assert.Contains("GRAPH", args);
    }
}
