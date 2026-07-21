using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>AutoCompleteBox 过滤与集合替换安全辅助</summary>
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
            // AutoCompleteBox 会在 SelectionModel 事务未结束时把高亮候选项写回 Text
            // 此时替换绑定集合会让 Avalonia 12.0.2 残留旧视图的 selected index，
            // commit/close 时可能崩溃
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

    private static void AttachPinyinFilter(AutoCompleteBox box)
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
