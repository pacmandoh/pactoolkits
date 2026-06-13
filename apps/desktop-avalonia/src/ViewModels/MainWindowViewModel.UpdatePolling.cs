using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private void RestartUpdatePolling()
    {
        _updatePollCts?.Cancel();
        _updatePollCts?.Dispose();
        _updatePollCts = null;

        if (!TryGetUpdatePollInterval(_updateSettings.Current, out var interval))
            return;

        _updatePollCts = new CancellationTokenSource();
        _ = RunUpdatePollingAsync(interval, _updatePollCts.Token);
    }

    private async Task RunUpdatePollingAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested || _disposed)
                return;

            await CheckAndPromptUpdateAsync(showNoUpdateToast: false, startupMode: false).ConfigureAwait(false);
        }
    }

    private static bool TryGetUpdatePollInterval(UpdateOptions options, out TimeSpan interval)
    {
        interval = TimeSpan.Zero;
        if (!options.AutoCheckOnStartup)
            return false;

        var configured = options.AutoCheckIntervalMinutes;
        if (configured > 0)
        {
            interval = TimeSpan.FromMinutes(Math.Clamp(configured, 1, 720));
            return true;
        }

        var channel = (options.Channel ?? string.Empty).Trim();
        var minutes = string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) ? 30 : 10;
        interval = TimeSpan.FromMinutes(minutes);
        return true;
    }

    private void OnUpdateSettingsChanged()
        => RestartUpdatePolling();
}
