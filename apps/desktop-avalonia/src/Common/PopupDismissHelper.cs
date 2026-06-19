using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.VisualTree;

namespace PacToolkits.Desktop.Avalonia.Common;

internal static class PopupDismissHelper
{
    public static bool IsInsideOpenPopupSurface(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        return visual.GetSelfAndVisualAncestors().Any(static ancestor =>
            ancestor is PopupRoot
                or OverlayPopupHost
                or ContextMenu
                or MenuItem
                || ancestor.GetType().Name is "LightDismissOverlayLayer" or "FlyoutPresenter");
    }

    public static void DismissOpenPopups(TopLevel topLevel)
    {
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
    }
}
