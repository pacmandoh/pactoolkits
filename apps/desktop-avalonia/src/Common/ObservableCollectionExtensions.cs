using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Syncs collection contents without calling Clear (Reset notification),
    /// so Avalonia DataGrid does not receive Reset and auto-select the first current cell.
    /// </summary>
    public static void ResetContents<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
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
}
