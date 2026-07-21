using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>ObservableCollection 安全替换扩展（避免 DataGrid Reset）</summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// 替换集合内容且不调用 Clear（避免 Reset 通知），
    /// 防止 Avalonia DataGrid 收到 Reset 后自动选中首个当前单元格
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
    /// 一次性替换整集合，且不发出 collection Reset 通知
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
