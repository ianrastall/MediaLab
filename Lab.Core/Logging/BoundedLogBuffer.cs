namespace Lab.Core.Logging;

/// <summary>
/// Thread-safe, fixed-capacity ring buffer of log lines. When full, the oldest line is
/// dropped to make room. This is the suite's guard against ffmpeg/Whisper output growing
/// UI memory without bound — callers append freely and the buffer keeps only the newest N.
/// </summary>
public sealed class BoundedLogBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines;
    private readonly int _capacity;

    /// <param name="capacity">Maximum number of retained lines (must be positive).</param>
    public BoundedLogBuffer(int capacity = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _lines = new Queue<string>(capacity);
    }

    /// <summary>Maximum number of lines retained.</summary>
    public int Capacity => _capacity;

    /// <summary>Lines currently retained.</summary>
    public int Count
    {
        get { lock (_gate) { return _lines.Count; } }
    }

    /// <summary>Total lines dropped over the buffer's lifetime due to capacity.</summary>
    public long DroppedCount { get; private set; }

    /// <summary>Appends a line, evicting the oldest if at capacity.</summary>
    public void Append(string line)
    {
        line ??= string.Empty;
        lock (_gate)
        {
            if (_lines.Count >= _capacity)
            {
                _lines.Dequeue();
                DroppedCount++;
            }

            _lines.Enqueue(line);
        }
    }

    /// <summary>Appends several lines in order.</summary>
    public void AppendRange(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            Append(line);
        }
    }

    /// <summary>Removes all retained lines. Does not reset <see cref="DroppedCount"/>.</summary>
    public void Clear()
    {
        lock (_gate) { _lines.Clear(); }
    }

    /// <summary>Returns a point-in-time copy of the retained lines, oldest first.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate) { return _lines.ToArray(); }
    }

    /// <summary>Joins the retained lines with newlines, oldest first.</summary>
    public string SnapshotText()
    {
        lock (_gate) { return string.Join(Environment.NewLine, _lines); }
    }
}
