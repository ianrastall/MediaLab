using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class ImageProbeParseTests
{
    [Fact]
    public void TryParseDimensions_ParsesWidthByHeight()
    {
        Assert.True(ImageProbeService.TryParseDimensions("3840x2160", out var d));
        Assert.Equal(new ImageDimensions(3840, 2160), d);
    }

    [Fact]
    public void TryParseDimensions_TrimsTrailingNewline()
    {
        Assert.True(ImageProbeService.TryParseDimensions(" 1920x1080 \n", out var d));
        Assert.Equal(new ImageDimensions(1920, 1080), d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("3840")]
    [InlineData("3840x")]
    [InlineData("axb")]
    [InlineData("0x2160")]
    [InlineData("3840x0")]
    public void TryParseDimensions_RejectsBadOutput(string? output)
    {
        Assert.False(ImageProbeService.TryParseDimensions(output, out _));
    }
}
