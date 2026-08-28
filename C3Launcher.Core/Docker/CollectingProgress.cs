namespace C3Launcher.Core.Docker;

/// <summary>
/// Synchronous IProgress. System.Progress&lt;T&gt; posts to the captured
/// SynchronizationContext, so on a UI thread its callbacks can land after the
/// awaited call has already returned.
/// </summary>
internal sealed class CollectingProgress<T> : IProgress<T>
{
    private readonly List<T> _items = [];
    private readonly Action<T>? _onReport;

    public CollectingProgress(Action<T>? onReport = null) => _onReport = onReport;

    public IReadOnlyList<T> Items
    {
        get { lock (_items) return _items.ToArray(); }
    }

    public void Report(T value)
    {
        lock (_items)
            _items.Add(value);

        _onReport?.Invoke(value);
    }
}
