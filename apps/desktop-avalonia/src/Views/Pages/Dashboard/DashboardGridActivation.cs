using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

/// <summary>
/// 将用户触发的 Dashboard 行激活与 DataGrid 布局期间产生的选择变更区分开
/// </summary>
internal sealed class DashboardGridActivation
{
    private readonly Func<DataGrid, object, Task> _activate;

    public DashboardGridActivation(Func<DataGrid, object, Task> activate)
    {
        _activate = activate;
    }

    public void Attach(DataGrid grid)
    {
        grid.PointerReleased += OnPointerReleased;
        grid.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    internal Task ActivateAsync(DataGrid grid, object? item)
        => item is null ? Task.CompletedTask : _activate(grid, item);

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled || e.InitialPressMouseButton != MouseButton.Left || sender is not DataGrid grid)
        {
            return;
        }

        var row = InputFocusHelper.FindAncestor<DataGridRow>(e.Source);
        if (row?.DataContext is null)
        {
            return;
        }

        e.Handled = true;
        await TryActivateAsync(grid, row.DataContext);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Enter || sender is not DataGrid grid || grid.SelectedItem is null)
        {
            return;
        }

        e.Handled = true;
        await TryActivateAsync(grid, grid.SelectedItem);
    }

    private async Task TryActivateAsync(DataGrid grid, object item)
    {
        try
        {
            await ActivateAsync(grid, item);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Dashboard", "dashboard.row_activation.fail", "Dashboard row activation failed", ex);
        }
    }
}
