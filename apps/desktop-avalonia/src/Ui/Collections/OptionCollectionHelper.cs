using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

namespace PacToolkits.Desktop.Avalonia.Ui.Collections;

/// <summary>在保留选中项语义的前提下同步可绑定选项集合</summary>
public static class OptionCollectionHelper
{
    public static bool ReplaceRaw(
        ObservableCollection<ViewModels.Pages.OptionItem> target,
        IEnumerable<string> values,
        StringComparison comparison = StringComparison.Ordinal)
        => Replace(target, LookupOptions.ToOptions(values), comparison);

    public static bool Replace(
        ObservableCollection<ViewModels.Pages.OptionItem> target,
        IEnumerable<ViewModels.Pages.OptionItem> values,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var next = values.ToList();

        if (IsSame(target, next, comparison))
        {
            return false;
        }

        target.Clear();
        foreach (var item in next)
        {
            target.Add(item);
        }

        return true;
    }

    private static bool IsSame(
        ObservableCollection<ViewModels.Pages.OptionItem> current,
        List<ViewModels.Pages.OptionItem> next,
        StringComparison comparison)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(current[i].Raw, next[i].Raw, comparison))
            {
                return false;
            }
        }

        return true;
    }
}
