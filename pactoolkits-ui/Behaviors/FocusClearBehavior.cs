using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Linq;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Behaviors;

public class FocusClearBehavior
{
    public static readonly AttachedProperty<bool> EnableProperty =
        AvaloniaProperty.RegisterAttached<FocusClearBehavior, Control, bool>("Enable");
    public static readonly AttachedProperty<bool> SuppressGridClearProperty =
        AvaloniaProperty.RegisterAttached<FocusClearBehavior, Control, bool>("SuppressGridClear");

    static FocusClearBehavior()
    {
        EnableProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.GetNewValue<bool>())
                c.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            else
                c.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
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
            return;

        var focused = TopLevel.GetTopLevel(scope)?.FocusManager?.GetFocusedElement();
        if (focused is not Control ctrl)
            return;

        var ownerAutoComplete = FindAncestor<AutoCompleteBox>(ctrl);
        if (ownerAutoComplete is not null)
        {
            if (IsInsideControl(e.Source, ownerAutoComplete))
                return;

            ownerAutoComplete.IsDropDownOpen = false;
        }

        if (IsInsideControl(e.Source, ctrl))
            return;

        TryClearDataGridSelections(scope, e.Source);

        if (scope is Control host)
            host.Focus();

        ctrl.Focusable = false;
        ctrl.Focusable = true;
        ctrl.IsTabStop = false;
        ctrl.IsTabStop = true;
    }

    private static bool IsInsideControl(object? source, Control target)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
                return true;

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
                return true;

            current = (current as StyledElement)?.Parent;
        }

        return false;
    }

    private static void TryClearDataGridSelections(InputElement scope, object? source)
    {
        if (IsInsideDataGrid(source))
            return;

        if (HasSuppressedGridClearAncestor(source))
            return;

        if (TopLevel.GetTopLevel(scope) is not TopLevel top)
            return;

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
                return true;

            current = (current as StyledElement)?.Parent;
        }

        return false;
    }

    private static T? FindAncestor<T>(object? source) where T : class
    {
        var current = source;
        while (current is not null)
        {
            if (current is T typed)
                return typed;

            current = (current as StyledElement)?.Parent;
        }

        return null;
    }
}
