using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class ColorMathTests
{
    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("ffffff", 255, 255, 255)]
    [InlineData("#C01822", 192, 24, 34)]
    [InlineData("c01822", 192, 24, 34)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("  #C01822  ", 192, 24, 34)] // trimmed
    [InlineData("#fff", 255, 255, 255)]      // shorthand
    [InlineData("#abc", 170, 187, 204)]      // shorthand expands each nibble
    public void TryParse_AcceptsValidForms(string input, byte r, byte g, byte b)
    {
        Assert.True(ColorMath.TryParse(input, out var color));
        Assert.Equal(new RgbColor(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xyz")]
    [InlineData("#12")]
    [InlineData("#1234")]
    [InlineData("#GGGGGG")]
    [InlineData("12345678")]
    public void TryParse_RejectsInvalidForms(string? input)
    {
        Assert.False(ColorMath.TryParse(input, out _));
    }

    [Fact]
    public void Normalize_ProducesCanonicalUpperHex()
    {
        Assert.Equal("#C01822", ColorMath.Normalize("c01822"));
        Assert.Equal("#C01822", ColorMath.Normalize("#C01822"));
        Assert.Equal("#FFFFFF", ColorMath.Normalize("fff"));
    }

    [Fact]
    public void ToFfmpegHex_UsesZeroXPrefix()
    {
        Assert.Equal("0xC01822", ColorMath.Parse("#C01822").ToFfmpegHex());
    }

    [Theory]
    [InlineData(255, 255, 255, 179, 179, 179)] // 255 * 0.7 = 178.5 -> 179 (away from zero)
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(200, 100, 50, 140, 70, 35)]    // exact multiples
    public void Darken_30Percent_ScalesAndRoundsEachChannel(
        byte r, byte g, byte b, byte er, byte eg, byte eb)
    {
        var result = ColorMath.Darken(new RgbColor(r, g, b), 0.30);
        Assert.Equal(new RgbColor(er, eg, eb), result);
    }

    [Fact]
    public void PeakFor_DarkensSelectedBarColorBy30Percent()
    {
        // #C01822 -> (192,24,34) * 0.7 = (134.4, 16.8, 23.8) -> (134, 17, 24) = #861118
        Assert.Equal("#861118", ColorMath.PeakFor(ColorMath.Parse("#C01822")).ToHex());
    }

    [Fact]
    public void Darken_ZeroFraction_IsIdentity()
    {
        var c = new RgbColor(123, 45, 67);
        Assert.Equal(c, ColorMath.Darken(c, 0.0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Darken_FractionOutOfRange_Throws(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMath.Darken(new RgbColor(10, 10, 10), fraction));
    }

    [Fact]
    public void Parse_InvalidInput_Throws()
    {
        Assert.Throws<FormatException>(() => ColorMath.Parse("not-a-color"));
    }
}
