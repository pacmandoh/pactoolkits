using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AutoCompleteHelper
{
    public static void AttachDrugOptionFilter(PlainAutoCompleteBox box)
        => AttachPinyinFilter(box);

    public static void AttachPinyinFilter(PlainAutoCompleteBox box)
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

    private static PlainAutoCompleteBox? ResolveBox(object? sender) =>
        sender as PlainAutoCompleteBox;

    public static bool HandleEnterCommitAndApply(
        UserControl owner,
        object? sender,
        KeyEventArgs e,
        string nextControlName,
        Action? applyAction)
    {
        if (ResolveBox(sender) is not { } box || e.Key != Key.Enter)
        {
            return false;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            CommitSuggestInput(box);
            applyAction?.Invoke();
            if (!string.IsNullOrWhiteSpace(nextControlName))
            {
                InputFocusHelper.FocusControlByName(owner, nextControlName);
            }
        }, DispatcherPriority.Input);
        return true;
    }

    public static bool HandleEnterCommitAndApplyAsync(
        UserControl owner,
        object? sender,
        KeyEventArgs e,
        string nextControlName,
        Func<PlainAutoCompleteBox, Task>? applyAsync)
    {
        if (ResolveBox(sender) is not { } box || e.Key != Key.Enter)
        {
            return false;
        }

        e.Handled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            CommitSuggestInput(box);
            if (applyAsync is not null)
            {
                await applyAsync(box);
            }

            if (!string.IsNullOrWhiteSpace(nextControlName))
            {
                InputFocusHelper.FocusControlByName(owner, nextControlName);
            }
        }, DispatcherPriority.Input);
        return true;
    }

    private static void CommitSuggestInput(PlainAutoCompleteBox box)
    {
        box.IsDropDownOpen = false;
        InputFocusHelper.CommitAutoCompleteInput(box.Box);
    }
}
