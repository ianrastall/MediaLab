using VideoLab.Core.Media;
using Lab.UI.Controls;

namespace MediaLab.Tests.Media;

public class FfmpegProgressParserTests
{
    private static void Feed(FfmpegProgressParser parser, IEnumerable<string> lines, out FfmpegProgress last, out int blocks)
    {
        last = default;
        blocks = 0;
        foreach (var line in lines)
        {
            if (parser.TryConsume(line, out var p))
            {
                last = p;
                blocks++;
            }
        }
    }

    [Fact]
    public void AccumulatesKeys_ThenEmitsOnProgressTerminator()
    {
        var parser = new FfmpegProgressParser();
        // A realistic block (note: out_time_us is the trustworthy field).
        string[] block =
        [
            "frame=60",
            "fps=60.00",
            "out_time_us=1000000",
            "out_time_ms=1000000", // misleadingly named — actually microseconds; must be ignored
            "out_time=00:00:01.000000",
            "progress=continue",
        ];

        Feed(parser, block, out var last, out var blocks);

        Assert.Equal(1, blocks);
        Assert.False(last.IsEnd);
        Assert.Equal(TimeSpan.FromSeconds(1), last.OutTime);
    }

    [Fact]
    public void UsesOutTimeUs_AsMicroseconds()
    {
        var parser = new FfmpegProgressParser();
        // 212.5 s -> 212_500_000 us. If misread as ms it would be ~2.4 days.
        Feed(parser, ["out_time_us=212500000", "progress=continue"], out var last, out _);

        Assert.Equal(212.5, last.OutTime!.Value.TotalSeconds, precision: 3);
    }

    [Fact]
    public void HandlesNotAvailableOutTime()
    {
        var parser = new FfmpegProgressParser();
        Feed(parser, ["out_time_us=N/A", "progress=continue"], out var last, out var blocks);

        Assert.Equal(1, blocks);
        Assert.Null(last.OutTime);
    }

    [Fact]
    public void DetectsEndBlock()
    {
        var parser = new FfmpegProgressParser();
        Feed(parser, ["out_time_us=5000000", "progress=end"], out var last, out _);

        Assert.True(last.IsEnd);
        Assert.Equal(TimeSpan.FromSeconds(5), last.OutTime);
    }

    [Fact]
    public void ParsesTwoConsecutiveBlocksIndependently()
    {
        var parser = new FfmpegProgressParser();
        string[] lines =
        [
            "out_time_us=1000000", "progress=continue",
            "out_time_us=2000000", "progress=continue",
        ];

        Feed(parser, lines, out var last, out var blocks);

        Assert.Equal(2, blocks);
        Assert.Equal(TimeSpan.FromSeconds(2), last.OutTime);
    }

    [Theory]
    [InlineData(50, 100, 50.0)]
    [InlineData(150, 100, 100.0)] // clamped
    [InlineData(0, 0, 0.0)]       // zero total -> 0, no divide-by-zero
    public void PercentOf_ClampsAndGuards(double posSec, double totalSec, double expected)
    {
        var p = new FfmpegProgress(TimeSpan.FromSeconds(posSec), false);
        Assert.Equal(expected, p.PercentOf(TimeSpan.FromSeconds(totalSec)), precision: 3);
    }

    [Fact]
    public void NonKeyValueLines_AreIgnored()
    {
        var parser = new FfmpegProgressParser();
        Assert.False(parser.TryConsume("", out _));
        Assert.False(parser.TryConsume("garbage-no-equals", out _));
        Assert.False(parser.TryConsume("=leadingequals", out _));
    }
}
