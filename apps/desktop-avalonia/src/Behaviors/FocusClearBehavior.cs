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

public class FocusClearBehavior
{
    private static WeakReference<TextBox>? _lastFocusedTextBox;

    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<FocusClearBehavior, Control, bool>("Enable");
    public static readonly AttachedProperty<bool> SuppressGridClearProperty =
        AvaloniaProperty.RegisterAttached<FocusClearBehavior, Control, bool>("SuppressGridClear");

    static FocusClearBehavior()
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

        if (PopupDismissHelper.IsInsideOpenPopupSurface(e.Source))
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(scope);
        if (topLevel is not null)
        {
            PopupDismissHelper.DismissOpenPopups(topLevel);
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

        // Clicking inside a grid should not trigger force-unfocus.
        // Otherwise the first click is often consumed by focus transfer.
        if (insideDataGrid)
        {
            return;
        }

        if (IsNaturalFocusTarget(e.Source))
        {
            return;
        }

        if (scope is Control host)
        {
            // Defer focus transfer so popup light-dismiss and the clicked control can process first.
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

            if (current.GetType().Name is "PlainAutoCompleteBox")
            {
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

        // MainWindow-level behavior should not clear all grid selections globally.
        // Otherwise dialog action button click may clear dialog grid selection before command executes.
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
