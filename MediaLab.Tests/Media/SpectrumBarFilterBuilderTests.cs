using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class SpectrumBarFilterBuilderTests
{
    private static VideoRenderJob Job() =>
        VideoRenderJob.Create("a.wav", "i.png", "o.mp4", "#C01822");

    [Fact]
    public void SeparatorBoundaries_Are39EvenlySpacedMultiplesOf96()
    {
        var bounds = SpectrumBarFilterBuilder.SeparatorBoundaries(3840, 40);

        Assert.Equal(39, bounds.Count);
        Assert.Equal(96, bounds[0]);
        Assert.Equal(3744, bounds[^1]);
        Assert.All(bounds, b => Assert.Equal(0, b % 96));
    }

    [Fact]
    public void SeparatorBoundaries_RejectNonDivisibleWidth()
    {
        Assert.Throws<ArgumentException>(() => SpectrumBarFilterBuilder.SeparatorBoundaries(3841, 40));
    }

    [Fact]
    public void Build_RendersTwoExtraColumnsAndCropsAtOffsetOne()
    {
        // 40 bars -> render 42 columns, crop 40 at x-offset 1 (drops DC + Nyquist).
        var graph = SpectrumBarFilterBuilder.Build(Job());

        Assert.Contains("showfreqs=s=42x2160", graph);
        Assert.Contains("crop=40:2160:1:0", graph);
    }

    [Fact]
    public void Build_UsesStatelessAnalyzerChain_HighpassAndFixedGain_NoAgc()
    {
        var graph = SpectrumBarFilterBuilder.Build(Job());

        Assert.Contains("highpass=f=30", graph);
        Assert.Contains("volume=12dB", graph);
        // The old drift came from an AGC; make sure we never emit one.
        Assert.DoesNotContain("dynaudnorm", graph);
        Assert.DoesNotContain("loudnorm", graph);
    }

    [Fact]
    public void Build_CascadesTwoHighpassStages_ToRejectSubsonicStuckBars()
    {
        // The two leftmost (lowest-freq) bars stuck on sustained sub-30 Hz energy that a single
        // 2-pole high-pass let through. We cascade two 2-pole stages (24 dB/oct at 30 Hz).
        var graph = SpectrumBarFilterBuilder.Build(Job());

        Assert.Equal(2, SpectrumBarFilterBuilder.HighpassStages);
        Assert.Contains("highpass=f=30:poles=2,highpass=f=30:poles=2", graph);
    }

    [Fact]
    public void Build_KeepsFinalAudioBranchUntouched()
    {
        var graph = SpectrumBarFilterBuilder.Build(Job());

        // asplit feeds an untouched [aout] (delivery) and [avisrc] (analyzer).
        Assert.Contains("[1:a]asplit=2[aout][avisrc]", graph);
        // The analyzer-only processing must attach to avisrc, never to aout.
        Assert.Contains("[avisrc]aformat", graph);
    }

    [Fact]
    public void Build_AppliesColorsOpacityAndNeighborScale()
    {
        var graph = SpectrumBarFilterBuilder.Build(Job());

        Assert.Contains("colors=0xC01822|0x861118", graph);   // bar | peak(30% darker)
        Assert.Contains("scale=3840:2160:flags=neighbor", graph);
        Assert.Contains("colorchannelmixer=aa=0.35", graph);
        Assert.Contains("overlay=0:0", graph);
        Assert.Contains("format=yuv420p[v]", graph);
    }

    [Fact]
    public void Build_EmitsCenteredSeparatorBoxes()
    {
        var graph = SpectrumBarFilterBuilder.Build(Job(), separatorWidth: 16);

        // First boundary at x=96, 16px wide, centered -> x = 96 - 8 = 88.
        // Transparent replace so gaps set alpha=0 directly (no extra 4K colorkey pass).
        Assert.Contains("drawbox=x=88:y=0:w=16:h=ih:color=0x00000000:replace=1:t=fill", graph);
    }

    [Fact]
    public void Build_AppliesColorkeyBeforeOpacity_SoBarsStayTransparent()
    {
        // colorkey OVERWRITES the alpha channel, so the 35% opacity must come AFTER it.
        // If reversed, bars render fully opaque (a real regression we hit once).
        var graph = SpectrumBarFilterBuilder.Build(Job());

        var keyIndex = graph.IndexOf("colorkey", StringComparison.Ordinal);
        var alphaIndex = graph.IndexOf("colorchannelmixer", StringComparison.Ordinal);

        Assert.True(keyIndex >= 0 && alphaIndex >= 0);
        Assert.True(keyIndex < alphaIndex, "colorkey must precede colorchannelmixer or bars lose transparency.");
    }

    [Fact]
    public void Build_UsesInvariantDecimalForOverlapAndOpacity()
    {
        var graph = SpectrumBarFilterBuilder.Build(Job());

        // Never a comma decimal, regardless of machine culture.
        Assert.Contains("overlap=0.875", graph);
        Assert.DoesNotContain("0,875", graph);
        Assert.DoesNotContain("aa=0,35", graph);
    }
}
