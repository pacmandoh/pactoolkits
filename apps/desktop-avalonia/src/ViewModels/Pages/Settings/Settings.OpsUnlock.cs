using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private const string OpsScope = UnlockScopes.SharedOps;

    private readonly DispatcherTimer _unlockStatusTimer;
    [ObservableProperty] private bool _isOpsUnlocked;

    public bool ShowUnlock => !IsOpsUnlocked;
    public bool ShowLock => IsOpsUnlocked;

    private bool CanUnlock() => !IsOpsUnlocked && CanPage;

    private bool CanLock() => IsOpsUnlocked && CanPage;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        await _unlockService.RequireUnlockAsync(
            OpsScope,
            "设置",
            "身份验证",
            UnlockScopes.SharedOpsHint,
            _pageWorkCts.Token).ConfigureAwait(true);
        RefreshOpsUnlock();
    }

    [RelayCommand(CanExecute = nameof(CanLock))]
    private Task LockAsync()
    {
        _unlockService.Lock(OpsScope);
        RefreshOpsUnlock();
        return Task.CompletedTask;
    }

    partial void OnIsOpsUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUnlock));
        OnPropertyChanged(nameof(ShowLock));
        RefreshOpsUnlockCommands();
    }

    private void RefreshOpsUnlock()
    {
        _unlockService.Refresh(OpsScope);
        var snapshot = _unlockService.GetSnapshot(OpsScope);
        IsOpsUnlocked = snapshot.IsUnlocked;

        if (IsOpsUnlocked || snapshot.CooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            if (!_unlockStatusTimer.IsEnabled)
            {
                _unlockStatusTimer.Start();
            }
        }
        else if (_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Stop();
        }

        RefreshOpsUnlockCommands();
    }

    private void RefreshOpsUnlockCommands()
        => RefreshCommands(UnlockCommand, LockCommand);

    private void OnUnlockChanged(string scopeKey)
    {
        if (!string.Equals(scopeKey, OpsScope, StringComparison.Ordinal))
        {
            return;
        }

        PostOnUi(RefreshOpsUnlock, DispatcherPriority.Background);
    }

    private void OnUnlockStatusTimerTick(object? sender, EventArgs e)
        => RefreshOpsUnlock();

    private void DisposeOpsUnlock()
    {
        _unlockService.StateChanged -= OnUnlockChanged;
        if (_unlockStatusTimer.IsEnabled)
        {
            _unlockStatusTimer.Stop();
        }

        _unlockStatusTimer.Tick -= OnUnlockStatusTimerTick;
    }
}
