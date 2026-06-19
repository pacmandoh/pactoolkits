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
        RunCommitAndApply(box, owner, nextControlName, applyAction);
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
        RunCommitAndApplyAsync(box, owner, nextControlName, applyAsync);
        return true;
    }

    public static void AttachCandidateCommitApply(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Action? applyAction)
    {
        box.CandidateCommitted += (_, _) =>
            RunCommitAndApplyImmediate(box, owner, nextControlName, applyAction);
    }

    public static void AttachCandidateCommitApplyAsync(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<PlainAutoCompleteBox, Task>? applyAsync)
    {
        box.CandidateCommitted += (_, _) =>
            _ = RunCommitAndApplyImmediateAsync(box, owner, nextControlName, applyAsync);
    }

    public static void RunCommitAndApply(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Action? applyAction)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CommitSuggestInput(box);
            applyAction?.Invoke();
            if (!string.IsNullOrWhiteSpace(nextControlName))
            {
                InputFocusHelper.FocusControlByName(owner, nextControlName);
            }
        }, DispatcherPriority.Input);
    }

    public static void RunCommitAndApplyAsync(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<PlainAutoCompleteBox, Task>? applyAsync)
    {
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
    }

    private static void RunCommitAndApplyImmediate(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Action? applyAction)
    {
        CommitSuggestInput(box);
        applyAction?.Invoke();
        if (!string.IsNullOrWhiteSpace(nextControlName))
        {
            InputFocusHelper.FocusControlByName(owner, nextControlName);
        }
    }

    private static async Task RunCommitAndApplyImmediateAsync(
        PlainAutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<PlainAutoCompleteBox, Task>? applyAsync)
    {
        CommitSuggestInput(box);
        if (applyAsync is not null)
        {
            await applyAsync(box).ConfigureAwait(true);
        }

        if (!string.IsNullOrWhiteSpace(nextControlName))
        {
            InputFocusHelper.FocusControlByName(owner, nextControlName);
        }
    }

    private static void CommitSuggestInput(PlainAutoCompleteBox box)
    {
        box.IsDropDownOpen = false;
        InputFocusHelper.CommitAutoCompleteInput(box.Box);
    }
}
