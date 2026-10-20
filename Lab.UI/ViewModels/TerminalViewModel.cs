using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lab.Core.Logging;

namespace Lab.UI.ViewModels;

/// <summary>
/// A bounded terminal/log surface shared by the suite's apps. Backed by a
/// <see cref="BoundedLogBuffer"/> so memory can't grow without limit, and it mirrors a capped
/// number of lines into an observable collection for display.
///
/// <para>Appends from any thread are enqueued lock-free and flushed to the UI collection in
/// batches by a single ~100 ms <see cref="DispatcherTimer"/> — so even a burst of ffmpeg output
/// produces at most ~10 UI updates/sec instead of one dispatcher hop per line.</para>
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject, IDisposable
{
    private readonly BoundedLogBuffer _buffer;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly int _maxVisibleLines;

    public TerminalViewModel(int bufferCapacity = 5000, int maxVisibleLines = 500)
    {
        _buffer = new BoundedLogBuffer(bufferCapacity);
        _maxVisibleLines = maxVisibleLines;

        // Created on the UI thread (VMs are constructed there), so Tick fires on the UI thread.
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _flushTimer.Tick += (_, _) => Flush();
        _flushTimer.Start();
    }

    /// <summary>Lines currently shown (capped at <c>maxVisibleLines</c>).</summary>
    public ObservableCollection<string> Lines { get; } = [];

    public void Dispose()
    {
        _flushTimer.Stop();
    }

    /// <summary>Appends a line; safe to call from any thread (flushed to the UI on a timer).</summary>
    public void Append(string line)
    {
        line ??= string.Empty;
        _buffer.Append(line);
        _pending.Enqueue(line);
    }

    private void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var added = false;
        while (_pending.TryDequeue(out var line))
        {
            Lines.Add(line);
            added = true;
        }

        if (!added)
        {
            return;
        }

        var excess = Lines.Count - _maxVisibleLines;
        if (excess <= 0)
        {
            return;
        }

        // Bulk-trim: clear and re-add the tail rather than calling RemoveAt(0) repeatedly.
        // RemoveAt(0) on ObservableCollection is O(N) per call (array shift + UI event),
        // so removing `excess` items is O(N²) for large bursts. Clear+re-add is O(N).
        var keep = new string[Lines.Count - excess];
        for (var i = 0; i < keep.Length; i++)
        {
            keep[i] = Lines[i + excess];
        }
        Lines.Clear();
        foreach (var l in keep)
        {
            Lines.Add(l);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _buffer.Clear();
        while (_pending.TryDequeue(out _))
        {
            // drain anything not yet flushed
        }

        Lines.Clear();
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(_buffer.SnapshotText());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
        {
            // Clipboard can be transiently locked by another process; ignore.
        }
    }
}
