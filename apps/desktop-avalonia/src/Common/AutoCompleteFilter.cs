using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using global::Avalonia.Controls;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// 药品 AutoComplete 候选过滤与排序回填
/// 回填集合后对齐选择模型（Avalonia #6128）
/// </summary>
public static class AutoCompleteFilter
{
    public static void AttachDrugOptionFilter(AutoCompleteBox box)
    {
        // None：展示 ItemsSource 原样；过滤/排序只走 Ranker 回填，避免与 ItemFilter 双轨
        box.FilterMode = AutoCompleteFilterMode.None;
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
            // 选中写回 Text 时再 Replace 会留下旧 SelectedIndex
            return;
        }

        var visible = DrugAutoCompleteRanker.FilterAndSort(
            catalog,
            searchText,
            static item => item.Raw,
            static item => item.Display);

        if (!OptionCollectionHelper.Replace(target, visible, StringComparison.Ordinal))
        {
            return;
        }

        AutoCompleteSelectionGuard.AlignForItemsSource(target);
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
