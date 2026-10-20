using Lab.Core.Logging;

namespace MediaLab.Tests.Logging;

public class BoundedLogBufferTests
{
    [Fact]
    public void Append_BeyondCapacity_DropsOldestAndCountsDrops()
    {
        var buffer = new BoundedLogBuffer(capacity: 3);
        buffer.AppendRange(["a", "b", "c", "d", "e"]);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { "c", "d", "e" }, buffer.Snapshot());
        Assert.Equal(2, buffer.DroppedCount);
    }

    [Fact]
    public void Snapshot_OnUnderfilledBuffer_ReturnsAllInOrder()
    {
        var buffer = new BoundedLogBuffer(capacity: 10);
        buffer.AppendRange(["x", "y"]);

        Assert.Equal(new[] { "x", "y" }, buffer.Snapshot());
        Assert.Equal(0, buffer.DroppedCount);
    }

    [Fact]
    public void SnapshotText_JoinsWithNewlines()
    {
        var buffer = new BoundedLogBuffer(capacity: 10);
        buffer.AppendRange(["one", "two", "three"]);

        Assert.Equal($"one{Environment.NewLine}two{Environment.NewLine}three", buffer.SnapshotText());
    }

    [Fact]
    public void Clear_EmptiesLines_ButKeepsDroppedCount()
    {
        var buffer = new BoundedLogBuffer(capacity: 2);
        buffer.AppendRange(["a", "b", "c"]); // drops "a"
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());
        Assert.Equal(1, buffer.DroppedCount);
    }

    [Fact]
    public void Append_NullLine_StoredAsEmpty()
    {
        var buffer = new BoundedLogBuffer(capacity: 2);
        buffer.Append(null!);

        Assert.Equal(new[] { string.Empty }, buffer.Snapshot());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLogBuffer(capacity));
    }

    [Fact]
    public async Task Append_IsThreadSafe_AndNeverExceedsCapacity()
    {
        var buffer = new BoundedLogBuffer(capacity: 100);
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                buffer.Append($"{t}-{i}");
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(100, buffer.Count);
        Assert.Equal(8000 - 100, buffer.DroppedCount);
    }
}
