using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.LogicalTree;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AutoCompleteCommit
{
    private static readonly ConditionalWeakTable<AutoCompleteBox, CommitState> CommitStates = new();

    internal static void ConfigureDrugBox(AutoCompleteBox box)
    {
        var state = GetState(box);
        if (state.DrugAutoCompleteConfigured)
        {
            return;
        }

        state.DrugAutoCompleteConfigured = true;
        state.Box = box;
        box.IsTextCompletionEnabled = false;
        box.Populated += state.OnPopulated;
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
        TryApplyActiveOrFirstMatch(box);
        box.IsDropDownOpen = false;
        InputFocusHelper.CommitAutoCompleteInput(box);
    }

    private static void TryApplyActiveOrFirstMatch(AutoCompleteBox box)
    {
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return;
        }

        var match = ResolveActiveOrFirstMatch(box);
        if (match is null)
        {
            return;
        }

        box.SelectedItem = match;
        box.Text = match.Raw;
    }

    private static OptionItem? ResolveActiveOrFirstMatch(AutoCompleteBox box)
    {
        if (FindPopupListBox(box) is { SelectedItem: OptionItem highlighted })
        {
            return highlighted;
        }

        if (box.SelectedItem is OptionItem selected
            && string.Equals(selected.Raw?.Trim(), box.Text?.Trim(), StringComparison.Ordinal))
        {
            return selected;
        }

        if (box.ItemsSource is not IEnumerable items)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (item is OptionItem option
                && (box.ItemFilter is null || box.ItemFilter(box.Text, option)))
            {
                return option;
            }
        }

        return null;
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
                state.InvokeCandidateCommitted,
                DispatcherPriority.Loaded);
        };

        box.AddHandler(InputElement.KeyDownEvent, state.OnPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        box.DropDownOpened += state.OnDropDownOpened;
        box.DropDownClosed += state.OnDropDownClosed;
    }

    private static ListBox? FindPopupListBox(AutoCompleteBox box)
    {
        var popup = box.GetLogicalDescendants()
                        .OfType<Popup>()
                        .FirstOrDefault(static candidate => candidate.Name == "PART_Popup")
                    ?? box.GetVisualDescendants()
                        .OfType<Popup>()
                        .FirstOrDefault(static candidate => candidate.Name == "PART_Popup");
        if (popup?.Child is not Control popupContent)
        {
            return null;
        }

        return popupContent as ListBox
               ?? popupContent.GetVisualDescendants()
                   .OfType<ListBox>()
                   .FirstOrDefault(static listBox => listBox.Name == "PART_SelectingItemsControl");
    }

    private static void ResetSuggestionScrollToTop(AutoCompleteBox box)
    {
        if (!box.IsDropDownOpen || FindPopupListBox(box) is not { } listBox)
        {
            return;
        }

        var scrollViewer = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        scrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0));
        Dispatcher.UIThread.Post(
            () => scrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, 0)),
            DispatcherPriority.Render);
    }

    private sealed class CommitState
    {
        internal AutoCompleteBox? Box;
        internal bool PipelineAttached;
        internal bool DrugAutoCompleteConfigured;
        internal bool PendingCandidateCommit;
        internal Action? CandidateCommitted;
        internal EventHandler<KeyEventArgs>? OnPreviewKeyDown;
        internal EventHandler? OnDropDownOpened;
        internal EventHandler? OnDropDownClosed;
        internal EventHandler<PopulatedEventArgs>? OnPopulated;
        internal TopLevel? DropDownPointerTopLevel;
        internal EventHandler<PointerPressedEventArgs>? DropDownPointerPressedHandler;
        internal EventHandler<PointerReleasedEventArgs>? DropDownPointerReleasedHandler;
        internal string? LastCommittedText;
        internal DateTimeOffset LastCommittedAt;

        internal CommitState()
        {
            OnPopulated = (_, _) =>
            {
                if (Box is { } box)
                {
                    Dispatcher.UIThread.Post(
                        () => ResetSuggestionScrollToTop(box),
                        DispatcherPriority.Loaded);
                }
            };
        }

        internal void InvokeCandidateCommitted()
        {
            var text = Box?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (string.Equals(text, LastCommittedText, StringComparison.Ordinal)
                && now - LastCommittedAt < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            LastCommittedText = text;
            LastCommittedAt = now;
            CandidateCommitted?.Invoke();
        }

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
