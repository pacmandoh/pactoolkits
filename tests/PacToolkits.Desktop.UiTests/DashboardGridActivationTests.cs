using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Views.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class DashboardGridActivationTests
{
    [AvaloniaFact]
    public void Enter_activates_selected_row()
    {
        var items = new ObservableCollection<string>(["drug-a"]);
        var activated = 0;
        var grid = CreateGrid("TrendGridOverview", items);
        var activation = new DashboardGridActivation((_, _) =>
        {
            activated++;
            return Task.CompletedTask;
        });
        activation.Attach(grid);

        var window = Show(grid);
        try
        {
            grid.SelectedItem = items[0];
            grid.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
                Source = grid,
            });

            Assert.Equal(1, activated);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Date_reload_selection_does_not_activate_client_row()
    {
        var items = new ObservableCollection<string>();
        var activated = 0;
        var grid = CreateGrid("TopClientsGridOverview", items);
        var activation = new DashboardGridActivation((_, _) =>
        {
            activated++;
            return Task.CompletedTask;
        });
        activation.Attach(grid);

        var window = Show(grid);
        try
        {
            items.ReplaceAll(["client-a", "client-b"]);
            FlushLayout(window);

            Assert.Equal(0, activated);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Trend_activation_reload_does_not_activate_txn_row()
    {
        var trendItems = new ObservableCollection<string>(["drug-a"]);
        var txnItems = new ObservableCollection<string>();
        var trendGrid = CreateGrid("TrendGridOverview", trendItems);
        var txnGrid = CreateGrid("RecentTxnGridOverview", txnItems);
        var activated = new List<string>();
        var activation = new DashboardGridActivation((grid, _) =>
        {
            activated.Add(grid.Name ?? string.Empty);
            if (ReferenceEquals(grid, trendGrid))
            {
                txnItems.ReplaceAll(["txn-a"]);
            }

            return Task.CompletedTask;
        });
        activation.Attach(trendGrid);
        activation.Attach(txnGrid);

        var window = Show(new StackPanel { Children = { trendGrid, txnGrid } });
        try
        {
            await activation.ActivateAsync(trendGrid, trendItems[0]);
            FlushLayout(window);

            Assert.Equal(["TrendGridOverview"], activated);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Client_reset_reload_does_not_activate_txn_row()
    {
        var clientItems = new ObservableCollection<string>(["client-a"]);
        var txnItems = new ObservableCollection<string>();
        var clientGrid = CreateGrid("TopClientsGridOverview", clientItems);
        var txnGrid = CreateGrid("RecentTxnGridOverview", txnItems);
        var activated = new List<string>();
        var activation = new DashboardGridActivation((grid, _) =>
        {
            activated.Add(grid.Name ?? string.Empty);
            if (ReferenceEquals(grid, clientGrid))
            {
                txnItems.ReplaceAll(["txn-filtered"]);
            }

            return Task.CompletedTask;
        });
        activation.Attach(clientGrid);
        activation.Attach(txnGrid);

        var window = Show(new StackPanel { Children = { clientGrid, txnGrid } });
        try
        {
            await activation.ActivateAsync(clientGrid, clientItems[0]);
            txnItems.ReplaceAll(["txn-all"]);
            FlushLayout(window);

            Assert.Equal(["TopClientsGridOverview"], activated);
        }
        finally
        {
            window.Close();
        }
    }

    private static DataGrid CreateGrid(string name, ObservableCollection<string> items)
        => new()
        {
            Name = name,
            ItemsSource = items,
            Width = 320,
            Height = 160,
        };

    private static Window Show(Control content)
    {
        var window = new Window { Content = content };
        window.Show();
        FlushLayout(window);
        return window;
    }

    private static void FlushLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
