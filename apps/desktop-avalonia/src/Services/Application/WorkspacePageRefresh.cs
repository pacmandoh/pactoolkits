using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class WorkspacePageRefresh
{
    public static bool CanRefreshPage(AppPageBase page)
        => page is not ISettingsPage
           && page is ITopBarActions { RefreshCommand: not null };

    public static async Task<bool> TryRefreshAsync(AppPageBase page)
    {
        if (page is not ITopBarActions top || top.RefreshCommand is not { } cmd)
        {
            return false;
        }

        if (!cmd.CanExecute(null))
        {
            return false;
        }

        if (cmd is IAsyncRelayCommand asyncCmd)
        {
            await asyncCmd.ExecuteAsync(null).ConfigureAwait(true);
        }
        else
        {
            cmd.Execute(null);
        }

        return RefreshSucceeded(page);
    }

    public static bool RefreshSucceeded(AppPageBase page)
        => page.PageDataAvailability is PageDataAvailability.Ready or PageDataAvailability.Stale;
}
