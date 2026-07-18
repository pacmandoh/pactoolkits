using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Replaces collection contents without calling Clear (Reset notification),
    /// so Avalonia DataGrid does not receive Reset and auto-select the first current cell.
    /// </summary>
    public static void ReplaceAll<T>(this IList<T> target, IReadOnlyList<T> items)
    {
        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(items[i]);
                continue;
            }

            if (!EqualityComparer<T>.Default.Equals(target[i], items[i]))
            {
                target.RemoveAt(i);
                target.Insert(i, items[i]);
            }
        }
    }

    /// <summary>
    /// Replaces the entire collection in one pass without issuing a collection Reset notification.
    /// </summary>
    public static void ReplaceAll<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
        => ((IList<T>)target).ReplaceAll(items);

    public static void SyncContentsInPlace<T>(
        this IList<T> target,
        IReadOnlyList<T> items,
        Action<T, T> update)
    {
        var sharedCount = Math.Min(target.Count, items.Count);
        for (var index = 0; index < sharedCount; index++)
        {
            update(target[index], items[index]);
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = target.Count; index < items.Count; index++)
        {
            target.Add(items[index]);
        }
    }

    public static void SyncContentsInPlace<T>(
        this ObservableCollection<T> target,
        IReadOnlyList<T> items,
        Action<T, T> update)
        => ((IList<T>)target).SyncContentsInPlace(items, update);
}
