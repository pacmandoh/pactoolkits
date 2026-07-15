using System.Collections.Generic;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

internal sealed class PageHistory<T>
{
    private const int MaxEntries = 32;
    private readonly List<T> _back = [];
    private readonly List<T> _forward = [];

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public T? BackTarget => CanGoBack ? _back[^1] : default;
    public T? ForwardTarget => CanGoForward ? _forward[^1] : default;

    public void Record(T? previous, T? current)
    {
        if (previous is null || current is null || EqualityComparer<T>.Default.Equals(previous, current))
        {
            return;
        }

        Push(_back, previous);
        _forward.Clear();
    }

    public void CompleteBack(T current)
    {
        if (!CanGoBack)
        {
            return;
        }

        _back.RemoveAt(_back.Count - 1);
        Push(_forward, current);
    }

    public void CompleteForward(T current)
    {
        if (!CanGoForward)
        {
            return;
        }

        _forward.RemoveAt(_forward.Count - 1);
        Push(_back, current);
    }

    private static void Push(List<T> entries, T item)
    {
        if (entries.Count > 0 && EqualityComparer<T>.Default.Equals(entries[^1], item))
        {
            return;
        }

        entries.Add(item);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveAt(0);
        }
    }
}
