using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AutoCompleteHelper
{
    public static void AttachDrugOptionFilter(AutoCompleteBox box)
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

    public static bool HandleEnterCommitAndApply(
        UserControl owner,
        object? sender,
        KeyEventArgs e,
        string nextControlName,
        Action? applyAction)
    {
        if (sender is not AutoCompleteBox box || e.Key != Key.Enter)
            return false;

        e.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            InputFocusHelper.CommitAutoCompleteInput(box);
            applyAction?.Invoke();
            if (!string.IsNullOrWhiteSpace(nextControlName))
                InputFocusHelper.FocusControlByName(owner, nextControlName);
        }, DispatcherPriority.Input);
        return true;
    }

    public static bool HandleEnterCommitAndApplyAsync(
        UserControl owner,
        object? sender,
        KeyEventArgs e,
        string nextControlName,
        Func<AutoCompleteBox, Task>? applyAsync)
    {
        if (sender is not AutoCompleteBox box || e.Key != Key.Enter)
            return false;

        e.Handled = true;
        Dispatcher.UIThread.Post(async () =>
        {
            InputFocusHelper.CommitAutoCompleteInput(box);
            if (applyAsync is not null)
                await applyAsync(box);
            if (!string.IsNullOrWhiteSpace(nextControlName))
                InputFocusHelper.FocusControlByName(owner, nextControlName);
        }, DispatcherPriority.Input);
        return true;
    }
}
