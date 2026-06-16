using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class OptionCollectionHelper
{
    public static bool ReplaceRaw(
        ObservableCollection<ViewModels.Pages.OptionItem> target,
        IEnumerable<string> values,
        StringComparison comparison = StringComparison.Ordinal)
        => Replace(target, LookupOptionLoader.ToOptions(values), comparison);

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
