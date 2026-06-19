using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.VisualTree;

namespace PacToolkits.Desktop.Avalonia.Common;

internal static class PopupDismissHelper
{
    public static bool ShouldSkipPopupDismiss(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        if (IsLightDismissOverlay(visual))
        {
            return false;
        }

        if (IsInsideMenuPointerTarget(visual))
        {
            return true;
        }

        return IsInsideOpenDropdownSurface(visual);
    }

    public static void DismissOpenPopups(TopLevel topLevel)
    {
        ContextMenuDismissTracker.CloseAllOpen();

        foreach (var control in topLevel.GetVisualDescendants().OfType<Control>())
        {
            switch (control)
            {
                case ComboBox combo when combo.IsDropDownOpen:
                    combo.IsDropDownOpen = false;
                    break;
                case CalendarDatePicker picker when picker.IsDropDownOpen:
                    picker.IsDropDownOpen = false;
                    break;
                case AutoCompleteBox autoComplete when autoComplete.IsDropDownOpen:
                    autoComplete.IsDropDownOpen = false;
                    break;
                case ContextMenu contextMenu when contextMenu.IsOpen:
                    contextMenu.Close();
                    break;
                case Popup popup when popup.IsOpen && popup.IsLightDismissEnabled:
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
            if (!popup.IsOpen)
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
                case PopupRoot or OverlayPopupHost when !underContextMenu:
                    return true;
            }
        }

        return false;
    }
}
