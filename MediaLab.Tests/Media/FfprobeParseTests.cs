using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class FfprobeParseTests
{
    [Theory]
    [InlineData("212.345000", 212.345)]
    [InlineData("1.000000", 1.0)]
    [InlineData("  1.5\n", 1.5)]
    [InlineData("0.000000", 0.0)]
    public void TryParseDurationSeconds_ParsesBareSeconds(string output, double expectedSeconds)
    {
        Assert.True(FfprobeService.TryParseDurationSeconds(output, out var duration));
        Assert.Equal(expectedSeconds, duration.TotalSeconds, precision: 3);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("212,345")] // comma is not invariant-culture decimal -> rejected
    public void TryParseDurationSeconds_RejectsNonNumeric(string? output)
    {
        Assert.False(FfprobeService.TryParseDurationSeconds(output, out _));
    }
}
