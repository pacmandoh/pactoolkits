using System.Collections.Generic;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;

namespace PacToolkits.Desktop.Avalonia.Common;

internal static class ContextMenuDismissTracker
{
    private static readonly HashSet<ContextMenu> OpenMenus = new();
    private static bool _handlersInstalled;

    public static void InstallGlobalHandlers()
    {
        if (_handlersInstalled)
        {
            return;
        }

        _handlersInstalled = true;

        MenuBase.OpenedEvent.AddClassHandler<ContextMenu>(OnContextMenuOpened);
        MenuBase.ClosedEvent.AddClassHandler<ContextMenu>(OnContextMenuClosed);
    }

    public static void AttachTopLevel(TopLevel topLevel)
    {
        topLevel.AddHandler(
            InputElement.PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public static void CloseAllOpen()
    {
        foreach (var menu in OpenMenus.ToArray())
        {
            if (!menu.IsOpen)
            {
                OpenMenus.Remove(menu);
                continue;
            }

            menu.Close();
        }
    }

    private static void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            OpenMenus.Add(menu);
        }
    }

    private static void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            OpenMenus.Remove(menu);
        }
    }

    private static void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (PopupDismissHelper.ShouldSkipPopupDismiss(e.Source))
        {
            return;
        }

        if (sender is TopLevel topLevel)
        {
            PopupDismissHelper.DismissOpenPopups(topLevel);
        }
    }
}
