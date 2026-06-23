using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AutoCompleteFilter
{
    public static void AttachDrugOptionFilter(AutoCompleteBox box)
    {
        AttachPinyinFilter(box);
        AutoCompleteCommit.ConfigureDrugBox(box);
    }

    public static bool HasDrugText(string? drugText)
        => !string.IsNullOrWhiteSpace((drugText ?? string.Empty).Trim());

    public static void RefreshVisibleOptions(
        ObservableCollection<OptionItem> target,
        IReadOnlyList<OptionItem> catalog,
        string? searchText)
    {
        if (IsCurrentCandidateText(target, searchText))
        {
            // AutoCompleteBox writes the highlighted candidate back to Text
            // while its SelectionModel transaction is still active. Replacing
            // the bound collection at that point leaves Avalonia 12.0.2 with a
            // selected index from the old view and can crash on commit/close.
            return;
        }

        var visible = DrugAutoCompleteRanker.FilterAndSort(
            catalog,
            searchText,
            static item => item.Raw,
            static item => item.Display);
        OptionCollectionHelper.Replace(target, visible, StringComparison.Ordinal);
    }

    internal static bool IsCurrentCandidateText(
        ObservableCollection<OptionItem> visibleOptions,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        foreach (var option in visibleOptions)
        {
            if (string.Equals(option.Raw, normalized, StringComparison.Ordinal)
                || string.Equals(option.Display, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static void AttachPinyinFilter(AutoCompleteBox box)
    {
        box.ItemFilter = static (search, item) =>
        {
            if (item is OptionItem option)
            {
                return PinyinInitialMatcher.IsMatch(search, option.Raw) ||
                       PinyinInitialMatcher.IsMatch(search, option.Display);
            }

            return item is not null &&
                   PinyinInitialMatcher.IsMatch(search, item.ToString());
        };
    }
}
