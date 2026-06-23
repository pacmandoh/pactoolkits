using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using PacToolkits.Application.TextSearch;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AutoCompleteHelper
{
    private static readonly ConditionalWeakTable<AutoCompleteBox, CommitState> CommitStates = new();

    public static void AttachDrugOptionFilter(AutoCompleteBox box)
        => AttachPinyinFilter(box);

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

    private static AutoCompleteBox? ResolveBox(object? sender) =>
        sender as AutoCompleteBox;

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
        Func<AutoCompleteBox, Task>? applyAsync)
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
        AutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Action? applyAction)
    {
        AttachCandidateCommitPipeline(box);
        GetState(box).CandidateCommitted = () =>
            RunCommitAndApplyImmediate(box, owner, nextControlName, applyAction);
    }

    public static void AttachCandidateCommitApplyAsync(
        AutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<AutoCompleteBox, Task>? applyAsync)
    {
        AttachCandidateCommitPipeline(box);
        GetState(box).CandidateCommitted = () =>
            _ = RunCommitAndApplyImmediateAsync(box, owner, nextControlName, applyAsync);
    }

    public static void RunCommitAndApply(
        AutoCompleteBox box,
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
        AutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<AutoCompleteBox, Task>? applyAsync)
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
        AutoCompleteBox box,
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
        AutoCompleteBox box,
        UserControl owner,
        string nextControlName,
        Func<AutoCompleteBox, Task>? applyAsync)
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

    private static void CommitSuggestInput(AutoCompleteBox box)
    {
        box.IsDropDownOpen = false;
        InputFocusHelper.CommitAutoCompleteInput(box);
    }

    private static CommitState GetState(AutoCompleteBox box)
        => CommitStates.GetValue(box, static _ => new CommitState());

    private static void AttachCandidateCommitPipeline(AutoCompleteBox box)
    {
        var state = GetState(box);
        if (state.PipelineAttached)
        {
            return;
        }

        state.PipelineAttached = true;
        state.Box = box;
        state.OnPreviewKeyDown = (_, e) =>
        {
            if (e.Key == Key.Enter && box.IsDropDownOpen)
            {
                state.PendingCandidateCommit = true;
            }
        };
        state.OnDropDownOpened = (_, _) => state.HookDropDownPointerHandlers();
        state.OnDropDownClosed = (_, _) =>
        {
            state.UnhookDropDownPointerHandlers();
            if (!state.PendingCandidateCommit)
            {
                return;
            }

            state.PendingCandidateCommit = false;
            Dispatcher.UIThread.Post(
                () => state.CandidateCommitted?.Invoke(),
                DispatcherPriority.Loaded);
        };

        box.AddHandler(InputElement.KeyDownEvent, state.OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        box.DropDownOpened += state.OnDropDownOpened;
        box.DropDownClosed += state.OnDropDownClosed;
    }

    private sealed class CommitState
    {
        internal AutoCompleteBox? Box;
        internal bool PipelineAttached;
        internal bool PendingCandidateCommit;
        internal Action? CandidateCommitted;
        internal EventHandler<KeyEventArgs>? OnPreviewKeyDown;
        internal EventHandler? OnDropDownOpened;
        internal EventHandler? OnDropDownClosed;
        internal TopLevel? DropDownPointerTopLevel;
        internal EventHandler<PointerPressedEventArgs>? DropDownPointerPressedHandler;
        internal EventHandler<PointerReleasedEventArgs>? DropDownPointerReleasedHandler;

        internal void HookDropDownPointerHandlers()
        {
            UnhookDropDownPointerHandlers();

            if (Box is null)
            {
                return;
            }

            DropDownPointerTopLevel = TopLevel.GetTopLevel(Box);
            if (DropDownPointerTopLevel is null)
            {
                return;
            }

            DropDownPointerPressedHandler ??= OnTopLevelPointerPressed;
            DropDownPointerReleasedHandler ??= OnTopLevelPointerReleased;
            DropDownPointerTopLevel.AddHandler(
                InputElement.PointerPressedEvent,
                DropDownPointerPressedHandler,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
            DropDownPointerTopLevel.AddHandler(
                InputElement.PointerReleasedEvent,
                DropDownPointerReleasedHandler,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        internal void UnhookDropDownPointerHandlers()
        {
            if (DropDownPointerTopLevel is null)
            {
                return;
            }

            if (DropDownPointerPressedHandler is not null)
            {
                DropDownPointerTopLevel.RemoveHandler(InputElement.PointerPressedEvent, DropDownPointerPressedHandler);
            }

            if (DropDownPointerReleasedHandler is not null)
            {
                DropDownPointerTopLevel.RemoveHandler(InputElement.PointerReleasedEvent, DropDownPointerReleasedHandler);
            }

            DropDownPointerTopLevel = null;
        }

        private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Box is null || !Box.IsDropDownOpen)
            {
                return;
            }

            if (IsInsideOpenDropDownSurface(e.Source))
            {
                PendingCandidateCommit = true;
            }
        }

        private void OnTopLevelPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (Box is null || e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            if (IsInsideOpenDropDownSurface(e.Source))
            {
                PendingCandidateCommit = true;
            }
        }

        private static bool IsInsideOpenDropDownSurface(object? source)
        {
            if (source is not Visual visual)
            {
                return false;
            }

            foreach (var ancestor in visual.GetVisualAncestors())
            {
                if (ancestor is PopupRoot or OverlayPopupHost)
                {
                    return true;
                }

                if (ancestor is ListBoxItem or ComboBoxItem or TreeViewItem)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
