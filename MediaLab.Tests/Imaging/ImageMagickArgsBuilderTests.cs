using ImageLab.Core.Imaging;

namespace MediaLab.Tests.Imaging;

public class ImageMagickArgsBuilderTests
{
    [Fact]
    public void Default_EmitsOnlyFillResize()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments());
        var joined = string.Join(" ", args);

        Assert.DoesNotContain("-crop", args);
        Assert.DoesNotContain("-brightness-contrast", args);
        Assert.DoesNotContain("-modulate", args);
        Assert.DoesNotContain("-channel", args);
        // Default ResizeMode is Fill to the 4K canvas.
        Assert.Contains("-resize 3840x2160^ -gravity center -extent 3840x2160", joined);
    }

    [Fact]
    public void Crop_EmittedWithRepage()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Crop = new CropRect(10, 20, 800, 600) });
        var joined = string.Join(" ", args);

        Assert.Contains("-crop 800x600+10+20 +repage", joined);
    }

    [Fact]
    public void BrightnessContrast_Combined()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Brightness = 10, Contrast = -20 });
        var joined = string.Join(" ", args);

        Assert.Contains("-brightness-contrast 10x-20", joined);
    }

    [Fact]
    public void SaturationAndChroma_MapToModulate()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Saturation = 120, Chroma = 90 });
        var joined = string.Join(" ", args);

        Assert.Contains("-modulate 100,120,90", joined);
    }

    [Fact]
    public void PerChannel_OnlyNonNeutralEmitted_WithChannelReset()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Red = 50, Blue = -100, ResizeMode = ResizeMode.None });
        var joined = string.Join(" ", args);

        Assert.Contains("-channel R -evaluate multiply 1.5", joined);  // 1 + 50/100
        Assert.Contains("-channel B -evaluate multiply 0", joined);    // 1 + (-100/100)
        Assert.DoesNotContain("-channel G", joined);                   // green neutral
        Assert.EndsWith("+channel", joined.TrimEnd());                 // channel reset emitted after edits
    }

    [Fact]
    public void Gamma_EmittedWhenNonNeutral_InvariantDecimal()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Gamma = 1.25, ResizeMode = ResizeMode.None });
        var joined = string.Join(" ", args);

        Assert.Contains("-gamma 1.25", joined);
        Assert.DoesNotContain("1,25", joined);
    }

    [Fact]
    public void Gamma_NeutralOmitted()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Gamma = 1.0, ResizeMode = ResizeMode.None });
        Assert.DoesNotContain("-gamma", args);
    }

    [Theory]
    [InlineData(ResizeMode.Fit, "-resize 1920x1080")]
    [InlineData(ResizeMode.Stretch, "-resize 1920x1080!")]
    public void ResizeModes_ProduceExpectedTokens(ResizeMode mode, string expected)
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments
        {
            ResizeMode = mode,
            TargetWidth = 1920,
            TargetHeight = 1080,
        });
        Assert.Contains(expected, string.Join(" ", args));
    }

    [Fact]
    public void ResizeNone_EmitsNoResize()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { ResizeMode = ResizeMode.None });
        Assert.DoesNotContain("-resize", args);
    }

    [Fact]
    public void ChannelFactor_UsesInvariantDecimal()
    {
        var args = ImageMagickArgsBuilder.BuildEditArgs(new ImageAdjustments { Red = 25, ResizeMode = ResizeMode.None });
        var joined = string.Join(" ", args);

        Assert.Contains("multiply 1.25", joined);
        Assert.DoesNotContain("1,25", joined);
    }
}
