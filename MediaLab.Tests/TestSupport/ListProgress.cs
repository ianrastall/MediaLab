namespace MediaLab.Tests.TestSupport;

/// <summary>
/// A synchronous <see cref="IProgress{T}"/> that captures every report on the calling thread.
/// Unlike <see cref="Progress{T}"/> it does not marshal to a SynchronizationContext, so reports
/// are deterministically available right after the awaited operation completes.
/// </summary>
internal sealed class ListProgress<T> : IProgress<T>
{
    private readonly List<T> _items = [];
    private readonly object _gate = new();

    public void Report(T value)
    {
        lock (_gate) { _items.Add(value); }
    }

    public IReadOnlyList<T> Items
    {
        get { lock (_gate) { return _items.ToArray(); } }
    }
}
