using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

internal static class MsfxAutoPanelGridInteraction
{
    public static void AttachDetailRowClick(
        DataGrid grid,
        bool allowRowBodyClick,
        Action<MsfxLink, object?> executeDetail,
        Func<MsfxLink, bool>? shouldSkip = null)
    {
        grid.AddHandler(
            InputElement.PointerPressedEvent,
            (sender, e) =>
            {
                if (sender is not DataGrid targetGrid)
                {
                    return;
                }

                if (targetGrid.DataContext is not MsfxLink vm)
                {
                    return;
                }

                if (shouldSkip?.Invoke(vm) == true)
                {
                    return;
                }

                if (e.Source is CheckBox or ToggleButton)
                {
                    return;
                }

                if (!DataGridInteractionHelper.TrySelectRowFromPointer(
                        targetGrid,
                        e.Source,
                        requireRowHeader: !allowRowBodyClick,
                        out var rowData,
                        out _))
                {
                    return;
                }

                executeDetail(vm, rowData);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }
}
