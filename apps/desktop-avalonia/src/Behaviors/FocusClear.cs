using System;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 点击空白处时清理焦点/弹层相关状态
///
/// 需避开 grid 内点击与对话框按钮的首次按下
/// </summary>
public class FocusClear
{
    private static WeakReference<TextBox>? _lastFocusedTextBox;

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<FocusClear, Control, bool>("Enable");
    public static readonly AttachedProperty<bool> SuppressGridClearProperty =
        AvaloniaProperty.RegisterAttached<FocusClear, Control, bool>("SuppressGridClear");

    static FocusClear()
    {
        EnableProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.GetNewValue<bool>())
            {
                c.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
                c.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            }
            else
            {
                c.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
                c.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
            }
        });
    }

    public static void SetEnable(AvaloniaObject element, bool value) =>
        element.SetValue(EnableProperty, value);

    public static bool GetEnable(AvaloniaObject element) =>
        element.GetValue(EnableProperty);

    public static void SetSuppressGridClear(AvaloniaObject element, bool value) =>
        element.SetValue(SuppressGridClearProperty, value);

    public static bool GetSuppressGridClear(AvaloniaObject element) =>
        element.GetValue(SuppressGridClearProperty);

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not InputElement scope)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(scope);
        if (topLevel is not null
            && e.Source is Visual sourceVisual
            && TopLevel.GetTopLevel(sourceVisual) is { } sourceTopLevel
            && !ReferenceEquals(sourceTopLevel, topLevel))
        {
            // Native popup 内容在独立 TopLevel。先让 popup 内控件处理指针事件，
            // 再跑 owner 级 focus / dismiss
            return;
        }

        if (topLevel is not null && !PopupDismissHelper.SkipPopupDismiss(e.Source))
        {
            PopupDismissHelper.DismissOpenPopups(topLevel);
        }

        if (PopupDismissHelper.SkipPopupDismiss(e.Source))
        {
            return;
        }

        var focused = topLevel?.FocusManager?.GetFocusedElement();
        if (focused is not Control ctrl)
        {
            return;
        }

        var insideDataGrid = IsInsideDataGrid(e.Source);

        var ownerAutoComplete = InputFocusHelper.FindAncestor<AutoCompleteBox>(ctrl);
        if (ownerAutoComplete is not null)
        {
            if (IsInsideControl(e.Source, ownerAutoComplete))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(ownerAutoComplete.Text))
            {
                AutoCompleteCommit.CommitPendingInput(ownerAutoComplete);
            }

            ownerAutoComplete.IsDropDownOpen = false;
        }

        if (IsInsideControl(e.Source, ctrl))
        {
            return;
        }

        if (ctrl is TextBox tb)
        {
            tb.ClearSelection();
        }

        TryClearDataGridSelections(scope, e.Source);

        // 点在 grid 内不应强制失焦，否则首次点击常被 focus 转移吃掉
        if (insideDataGrid)
        {
            return;
        }

        if (IsNaturalFocusTarget(e.Source))
        {
            return;
        }

        // 按钮等点击目标需保留 focus 以跑 Command/Click；此处若抢 host focus
        // 会吃掉对话框提交按钮的首次按下
        if (IsInteractiveClickTarget(e.Source))
        {
            return;
        }

        if (scope is Control host)
        {
            // 推迟 focus 转移，让 popup light-dismiss 与被点控件先处理
            Dispatcher.UIThread.Post(() => host.Focus(), DispatcherPriority.Input);
        }
    }

    private static void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not TextBox current)
        {
            return;
        }

        if (_lastFocusedTextBox is not null &&
            _lastFocusedTextBox.TryGetTarget(out var previous) &&
            !ReferenceEquals(previous, current))
        {
            previous.ClearSelection();
        }

        _lastFocusedTextBox = new WeakReference<TextBox>(current);
    }

    private static bool IsInsideControl(object? source, Control target)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = (current as StyledElement)?.Parent;
        }

        return false;
    }

    private static bool IsInsideDataGrid(object? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is DataGrid or DataGridRow or DataGridCell or DataGridColumnHeader or ScrollBar)
            {
                return true;
            }

            current = (current as StyledElement)?.Parent;
        }

        return false;
    }

    private static bool IsNaturalFocusTarget(object? source)
    {
        for (var current = source; current is not null; current = (current as StyledElement)?.Parent)
        {
            switch (current)
            {
                case TextBox:
                case ComboBox:
                case AutoCompleteBox:
                case CalendarDatePicker:
                case NumericUpDown:
                    return true;
            }
        }

        return false;
    }

    private static bool IsInteractiveClickTarget(object? source)
    {
        for (var current = source; current is not null; current = (current as StyledElement)?.Parent)
        {
            switch (current)
            {
                case Button:
                case MenuItem:
                case TabItem:
                    return true;
            }
        }

        return false;
    }

    private static void TryClearDataGridSelections(InputElement scope, object? source)
    {
        if (IsInsideDataGrid(source))
        {
            return;
        }

        // MainWindow 级行为不应全局清掉所有 grid 选中，
        // 否则对话框操作按钮点击可能在 Command 执行前清掉对话框内 grid 选中
        if (scope is TopLevel)
        {
            return;
        }

        if (scope is Control scopeControl && scopeControl.GetValue(SuppressGridClearProperty))
        {
            return;
        }

        if (HasSuppressedGridClearAncestor(source))
        {
            return;
        }

        if (TopLevel.GetTopLevel(scope) is not TopLevel top)
        {
            return;
        }

        foreach (var grid in top.GetVisualDescendants().OfType<DataGrid>())
        {
            DataGridInteractionHelper.ClearSelection(grid);
        }
    }

    private static bool HasSuppressedGridClearAncestor(object? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Control c && c.GetValue(SuppressGridClearProperty))
            {
                return true;
            }

            current = (current as StyledElement)?.Parent;
        }

        return false;
    }

}
