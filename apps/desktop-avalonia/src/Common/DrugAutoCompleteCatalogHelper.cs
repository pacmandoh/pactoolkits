using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DrugAutoCompleteCatalogHelper
{
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
}
