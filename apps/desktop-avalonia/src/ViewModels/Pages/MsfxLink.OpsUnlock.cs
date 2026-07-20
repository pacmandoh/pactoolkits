using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink
{
    private const string OpsScope = UnlockScopes.SharedOps;

    [ObservableProperty] private bool _isOpsUnlocked;
    private DateTimeOffset _opsCooldownUntilUtc;

    public bool ShowUnlock => (IsRunPage || IsQueuePage) && !IsOpsUnlocked;
    public bool ShowLock => (IsRunPage || IsQueuePage) && IsOpsUnlocked;

    private bool CanUnlock()
        => (IsRunPage || IsQueuePage)
           && !IsOpsUnlocked
           && !IsAutoBoardBusy
           && !IsMappingBusy;

    private bool CanLock()
        => (IsRunPage || IsQueuePage)
           && IsOpsUnlocked
           && !IsAutoBoardBusy
           && !IsMappingBusy;

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        await _unlockService.RequireUnlockAsync(
            OpsScope,
            "码上放心联调",
            "身份验证",
            UnlockScopes.SharedOpsHint).ConfigureAwait(false);
        await RunOnUiAsync(RefreshOpsUnlock).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanLock))]
    private Task LockAsync()
    {
        _unlockService.Lock(OpsScope);
        RefreshOpsUnlock();
        return Task.CompletedTask;
    }

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        RefreshOpsUnlock();
        return base.OnPageActivatedAsync(ct);
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
        _opsCooldownUntilUtc = snapshot.CooldownUntilUtc;

        if (IsOpsUnlocked || _opsCooldownUntilUtc > DateTimeOffset.UtcNow)
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

    private void OnUnlockStatusTimerTick(object? sender, EventArgs e)
        => RefreshOpsUnlock();
}
