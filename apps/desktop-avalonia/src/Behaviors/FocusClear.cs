using System;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 主窗壳层指针策略：键盘焦点落点（K）、DataGrid 原生选中；与 PopupDismissHelper 分工处理临时 UI（T）
///
/// 子树 <see cref="SuppressGridClearProperty"/> 不参与原生表清选
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
                c.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, true);
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
            return;
        }

        if (PopupDismissHelper.SkipPopupDismiss(e.Source))
        {
            return;
        }

        // 关下拉由 PopupDismissHelper.AttachTopLevel 统一处理；此处只做 K / 原生选中 / ACB 提交

        var focused = topLevel?.FocusManager?.GetFocusedElement() as Control;
        if (focused is not null)
        {
            LeaveAutoCompleteIfNeeded(focused, e.Source);

            // 仅可编辑输入在自身内点击时保留；MainWindow/页 root 不得挡住表外清选与抬 K
            if (IsEditableFocusTarget(focused) && IsInside(e.Source, focused))
            {
                return;
            }

            if (focused is TextBox tb)
            {
                tb.ClearSelection();
            }
        }

        // 表内清 peer、保留当前表（保证首次点行）；表外清全部无 suppress 表
        var activeGrid = FindOwningDataGrid(e.Source);
        if (activeGrid is not null)
        {
            ClearNativeGridSelections(scope, activeGrid, keep: activeGrid);
            return;
        }

        ClearNativeGridSelections(scope, e.Source, keep: null);

        if (IsProtectedClickTarget(e.Source))
        {
            return;
        }

        ClearKeyboardFocus(scope as Control ?? topLevel as Control);
    }

    /// <summary>表内 peer 清选；headless 命中不稳时测试直调</summary>
    internal static void ClearPeerSelectionsForTests(InputElement scope, DataGrid activeGrid)
        => ClearNativeGridSelections(scope, activeGrid, keep: activeGrid);

    private static void LeaveAutoCompleteIfNeeded(Control focused, object? clickSource)
    {
        var owner = InputFocusHelper.FindAncestor<AutoCompleteBox>(focused)
            ?? focused as AutoCompleteBox;
        if (owner is null || IsInside(clickSource, owner))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(owner.Text))
        {
            // Commit 内会关下拉；空白文本的关闭由 PopupDismissHelper 统一做
            AutoCompleteCommit.CommitPendingInput(owner);
        }
    }

    private static void ClearKeyboardFocus(Control? host)
    {
        if (host is null)
        {
            return;
        }

        // 同步 Focus：Post 会被后续 Pointer 盖掉（标题区抬输入焦点需要）
        if (!host.Focusable)
        {
            host.Focusable = true;
        }

        host.Focus();
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

    private static void ClearNativeGridSelections(InputElement scope, object? suppressProbe, DataGrid? keep)
    {
        // Enable 所在控件是 pointer 祖先时，probe 行走会覆盖；不需再读 scope 上 Suppress
        if (HasSuppressedGridClearAncestor(suppressProbe))
        {
            return;
        }

        if (TopLevel.GetTopLevel(scope) is not TopLevel top)
        {
            return;
        }

        foreach (var grid in top.GetVisualDescendants().OfType<DataGrid>())
        {
            if (keep is not null && ReferenceEquals(grid, keep))
            {
                continue;
            }

            if (HasSuppressedGridClearAncestor(grid))
            {
                continue;
            }

            DataGridInteractionHelper.ClearSelection(grid);
        }
    }

    private static DataGrid? FindOwningDataGrid(object? source)
    {
        if (source is not Visual visual)
        {
            return null;
        }

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is DataGrid grid)
            {
                return grid;
            }
        }

        return null;
    }

    private static bool HasSuppressedGridClearAncestor(object? node)
    {
        if (node is not Visual visual)
        {
            return false;
        }

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is Control c && c.GetValue(SuppressGridClearProperty))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInside(object? source, Control target)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ReferenceEquals(ancestor, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEditableFocusTarget(Control focused)
        => focused is TextBox
            or ComboBox
            or AutoCompleteBox
            or CalendarDatePicker
            or NumericUpDown;

    // 可编辑 / Button 等：不抢 host Focus，保证首次点击生效
    private static bool IsProtectedClickTarget(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is Control c && IsEditableFocusTarget(c))
            {
                return true;
            }

            switch (ancestor)
            {
                case Button:
                case MenuItem:
                case TabItem:
                    return true;
            }
        }

        return false;
    }
}
