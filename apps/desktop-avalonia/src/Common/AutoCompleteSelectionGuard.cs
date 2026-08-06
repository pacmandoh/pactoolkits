using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.LogicalTree;
using global::Avalonia.VisualTree;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// Avalonia #6128：ItemsSource 变更后 SelectionModel 下标可能越界，CloseDropDown 会崩
/// DropDownClosing / Replace 后仅在 index 失步时重绑选择源
/// </summary>
internal static class AutoCompleteSelectionGuard
{
    private static readonly ConditionalWeakTable<AutoCompleteBox, object?> Registered = new();
    private static readonly List<WeakReference<AutoCompleteBox>> Boxes = new();
    private static readonly object Gate = new();

    public static void Register(AutoCompleteBox box)
    {
        if (!Registered.TryAdd(box, null))
        {
            return;
        }

        lock (Gate)
        {
            Prune_NoLock();
            Boxes.Add(new WeakReference<AutoCompleteBox>(box));
        }

        // CloseDropDown 之前：仅修失步 index
        box.DropDownClosing += (_, _) => AlignSelector(box);
    }

    /// <summary>集合 Clear/Add 之后：若 SelectedIndex 越界则重绑选择源</summary>
    public static void AlignForItemsSource(IEnumerable? itemsSource)
    {
        if (itemsSource is null)
        {
            return;
        }

        foreach (var box in Snapshot())
        {
            if (ReferenceEquals(box.ItemsSource, itemsSource))
            {
                AlignSelector(box);
            }
        }
    }

    internal static SelectingItemsControl? FindSelector(AutoCompleteBox box)
    {
        var popup = box.GetLogicalDescendants()
                        .OfType<Popup>()
                        .FirstOrDefault(static p => p.Name == "PART_Popup")
                    ?? box.GetVisualDescendants()
                        .OfType<Popup>()
                        .FirstOrDefault(static p => p.Name == "PART_Popup");

        if (popup?.Child is SelectingItemsControl direct)
        {
            return direct;
        }

        if (popup?.Child is Control content)
        {
            var named = content.GetVisualDescendants()
                .OfType<SelectingItemsControl>()
                .FirstOrDefault(static c => c.Name == "PART_SelectingItemsControl");
            if (named is not null)
            {
                return named;
            }
        }

        return box.GetLogicalDescendants()
                   .OfType<SelectingItemsControl>()
                   .FirstOrDefault(static c => c.Name == "PART_SelectingItemsControl")
               ?? box.GetVisualDescendants()
                   .OfType<SelectingItemsControl>()
                   .FirstOrDefault(static c => c.Name == "PART_SelectingItemsControl");
    }

    private static void AlignSelector(AutoCompleteBox box)
    {
        if (FindSelector(box) is not { } selector)
        {
            return;
        }

        var index = selector.SelectedIndex;
        // 仅修 #6128 失步（index ≥ 集合长度）；合法高亮留给 CloseDropDown / 选中提交流程
        if (index < 0 || index < selector.ItemCount)
        {
            return;
        }

        var items = selector.ItemsSource;
        selector.ItemsSource = null;
        if (items is not null)
        {
            selector.ItemsSource = items;
        }
    }

    private static List<AutoCompleteBox> Snapshot()
    {
        lock (Gate)
        {
            Prune_NoLock();
            var list = new List<AutoCompleteBox>(Boxes.Count);
            foreach (var weak in Boxes)
            {
                if (weak.TryGetTarget(out var box))
                {
                    list.Add(box);
                }
            }

            return list;
        }
    }

    private static void Prune_NoLock()
    {
        for (var i = Boxes.Count - 1; i >= 0; i--)
        {
            if (!Boxes[i].TryGetTarget(out _))
            {
                Boxes.RemoveAt(i);
            }
        }
    }
}
