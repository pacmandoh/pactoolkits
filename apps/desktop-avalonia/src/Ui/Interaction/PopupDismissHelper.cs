using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;

namespace PacToolkits.Desktop.Avalonia.Ui.Interaction;

/// <summary>
/// TopLevel 内打开中的 Popup / 下拉 light-dismiss
/// 不强制关 ACB（由控件自身 light-dismiss 与 SelectionGuard 收口）
/// </summary>
internal static class PopupDismissHelper
{
    public static void AttachTopLevel(TopLevel topLevel)
    {
        topLevel.AddHandler(
            InputElement.PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public static bool SkipPopupDismiss(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        // 候选面优先：Overlay 下命中源偶发落在 LightDismissOverlayLayer
        if (IsInsideOpenDropdownSurface(visual))
        {
            return true;
        }

        if (IsLightDismissOverlay(visual))
        {
            return false;
        }

        return IsInsideMenuPointerTarget(visual);
    }

    public static void DismissOpenPopups(TopLevel topLevel)
    {
        foreach (var control in topLevel.GetVisualDescendants().OfType<Control>().ToList())
        {
            switch (control)
            {
                case ComboBox combo when combo.IsDropDownOpen:
                    combo.IsDropDownOpen = false;
                    break;
                case CalendarDatePicker picker when picker.IsDropDownOpen:
                    picker.IsDropDownOpen = false;
                    break;
                case ContextMenu contextMenu when contextMenu.IsOpen:
                    contextMenu.Close();
                    break;
                case Popup popup when popup.IsOpen && popup.IsLightDismissEnabled:
                    // 不关 Combo/ACB/DatePicker 的 PART_Popup（再关会走 CloseDropDown）
                    if (IsSelectorDropDownPopup(popup))
                    {
                        break;
                    }

                    popup.IsOpen = false;
                    break;
            }
        }

        CloseContextMenuPopupsInLayer(topLevel);
    }

    private static void CloseContextMenuPopupsInLayer(TopLevel topLevel)
    {
        foreach (var popup in topLevel.GetVisualDescendants().OfType<Popup>())
        {
            if (!popup.IsOpen || IsSelectorDropDownPopup(popup))
            {
                continue;
            }

            var hostsMenu = popup.Child?.GetVisualDescendants().OfType<MenuItem>().Any() == true
                || popup.Child is ContextMenu
                || popup.Child?.GetVisualDescendants().OfType<ContextMenu>().Any() == true;

            if (hostsMenu)
            {
                popup.IsOpen = false;
            }
        }
    }

    /// <summary>模板下拉 Popup（关它等于走控件 CloseDropDown）</summary>
    private static bool IsSelectorDropDownPopup(Popup popup)
        => popup.Name is "PART_Popup"
           || popup.TemplatedParent is AutoCompleteBox or ComboBox or CalendarDatePicker
           || popup.PlacementTarget is AutoCompleteBox or ComboBox or CalendarDatePicker;

    private static bool IsLightDismissOverlay(Visual visual)
        => visual.GetSelfAndVisualAncestors().Any(static ancestor =>
            ancestor.GetType().Name is "LightDismissOverlayLayer");

    private static bool IsInsideMenuPointerTarget(Visual visual)
        => visual.GetSelfAndVisualAncestors().Any(static ancestor => ancestor is MenuItem);

    private static bool IsInsideOpenDropdownSurface(Visual visual)
    {
        var underContextMenu = visual.GetSelfAndVisualAncestors().Any(static ancestor =>
            ancestor is ContextMenu or MenuItem);

        foreach (var ancestor in visual.GetSelfAndVisualAncestors())
        {
            switch (ancestor)
            {
                case ComboBox combo when combo.IsDropDownOpen:
                case AutoCompleteBox autoComplete when autoComplete.IsDropDownOpen:
                case CalendarDatePicker picker when picker.IsDropDownOpen:
                    return true;
                // 候选行在 Overlay 上；无 PopupRoot 名时 ListBoxItem 仍属下拉宿主
                case ListBoxItem or ComboBoxItem or TreeViewItem
                    when !underContextMenu && IsUnderOpenDropdownHost(ancestor):
                    return true;
                case PopupRoot or OverlayPopupHost when !underContextMenu:
                    return true;
            }
        }

        return false;
    }

    private static bool IsUnderOpenDropdownHost(Visual itemContainer)
    {
        foreach (var ancestor in itemContainer.GetSelfAndVisualAncestors())
        {
            switch (ancestor)
            {
                case PopupRoot or OverlayPopupHost:
                case ComboBox combo when combo.IsDropDownOpen:
                case AutoCompleteBox autoComplete when autoComplete.IsDropDownOpen:
                    return true;
            }
        }

        return false;
    }

    private static void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (SkipPopupDismiss(e.Source))
        {
            return;
        }

        if (sender is TopLevel topLevel)
        {
            DismissOpenPopups(topLevel);
        }
    }
}
