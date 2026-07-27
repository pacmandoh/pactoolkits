using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private enum Tab
    {
        Database = 0,
        ClientAliases = 1,
        TraceCodeRule = 2,
        UiBehavior = 3,
        Updates = 4,
        Logging = 5,
        MsfxApi = 6,
        Agents = 7
    }

    private static readonly string[] TabTitles =
    [
        "PostgreSQL 设置",
        "客户端别名映射",
        "追溯码校验规则",
        "界面行为",
        "应用更新",
        "日志与诊断",
        "码上放心 API",
        "自动化集成"
    ];

    private int _unsavedMask;
    private int _activeTabIndex = -1;
    private Dictionary<string, string>? _clientAliasEditBaseline;
    private CancellationTokenSource? _loggingAutoSaveCts;
    private CancellationTokenSource? _updateAutoCheckSaveCts;
    private CancellationTokenSource? _updateChannelSaveCts;

    public event Action? UnsavedChanged;

    public bool HasUnsavedChanges => _unsavedMask != 0;

    public bool IsTabDirty(int tabIndex)
        => tabIndex >= 0
           && tabIndex < TabTitles.Length
           && (_unsavedMask & (1 << tabIndex)) != 0;

    public void OnTabEntered(int tabIndex)
    {
        _activeTabIndex = tabIndex;
        ReloadClientAliasesIfVisible("client_alias.reload.tab_enter_fail");
    }

    private void ReloadClientAliasesIfVisible(string failEvent)
    {
        if (_activeTabIndex == (int)Tab.ClientAliases)
        {
            RequestClientAliasReload(failEvent);
        }
    }

    public async Task<bool> ConfirmLeaveTabAsync(int tabIndex)
    {
        if (!IsTabDirty(tabIndex))
        {
            return true;
        }

        var choice = await _dialog.Alert(
            AlertBuilder<bool?>.Create("有未保存的更改", $"「{TabTitles[tabIndex]}」中的修改尚未保存")
                .DiscardOrSave("保存并继续", "放弃更改"));

        if (choice is null)
        {
            return false;
        }

        if (choice == true)
        {
            return await SaveTabAsync((Tab)tabIndex);
        }

        RevertTab((Tab)tabIndex);
        return true;
    }

    public async Task<bool> TrySaveOrDiscardAllAsync()
    {
        RefreshUnsaved();
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var names = string.Join("、", GetDirtyTabTitles());
        var choice = await _dialog.Alert(
            AlertBuilder<bool?>.Create("设置页有未保存的更改", $"以下板块尚未保存：{names}")
                .DiscardOrSave("全部保存", "放弃全部"));

        if (choice is null)
        {
            return false;
        }

        if (choice == true)
        {
            foreach (var tab in GetDirtyTabs().ToArray())
            {
                if (!await SaveTabAsync(tab))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var tab in GetDirtyTabs().ToArray())
        {
            RevertTab(tab);
        }

        return true;
    }

    private async Task<bool> SaveTabAsync(Tab tab)
        => tab switch
        {
            Tab.Database => await ApplyDbConfigAsync(),
            Tab.ClientAliases => await ApplyClientAliasesAsync(),
            Tab.TraceCodeRule => await ApplyTraceCodeRuleAsync(),
            Tab.Updates => await ApplyUpdateOptionsAsync(),
            Tab.MsfxApi => await ApplyMsfxApiConfigAsync(),
            Tab.Agents => await ApplyAgentsSettingsAsync(showSuccessToast: false),
            Tab.Logging => await ApplyLoggingOptionsAsync(silent: false),
            Tab.UiBehavior => true,
            _ => true
        };

    private void RevertTab(Tab tab)
    {
        switch (tab)
        {
            case Tab.Database:
                RestoreDatabaseFields(_settings.AppliedDb);
                break;
            case Tab.ClientAliases:
                _clientAliasEditBaseline = null;
                IsClientAliasEditMode = false;
                IsClientAliasReadOnly = true;
                RequestClientAliasReload("client_alias.reload.after_revert_fail");
                break;
            case Tab.TraceCodeRule:
                SyncTraceCodeRule();
                break;
            case Tab.Updates:
                SyncUpdateOptions();
                break;
            case Tab.MsfxApi:
                SyncMsfxApi();
                break;
            case Tab.Agents:
                CancelModuleAutoSaves();
                SyncAgentsConfig();
                break;
            case Tab.UiBehavior:
                SyncUiBehavior();
                break;
            case Tab.Logging:
                SyncLogging();
                break;
        }

        RefreshUnsaved();
    }

    private IEnumerable<Tab> GetDirtyTabs()
    {
        for (var i = 0; i < TabTitles.Length; i++)
        {
            if (IsTabDirty(i))
            {
                yield return (Tab)i;
            }
        }
    }

    private IEnumerable<string> GetDirtyTabTitles()
        => GetDirtyTabs().Select(tab => TabTitles[(int)tab]);

    private void RefreshUnsaved()
    {
        var nextMask = 0;
        if (IsDatabaseDirty())
        {
            nextMask |= 1 << (int)Tab.Database;
        }

        if (IsClientAliasesDirty())
        {
            nextMask |= 1 << (int)Tab.ClientAliases;
        }

        if (IsTraceCodeRuleDirty())
        {
            nextMask |= 1 << (int)Tab.TraceCodeRule;
        }

        if (IsUpdateDraftDirty())
        {
            nextMask |= 1 << (int)Tab.Updates;
        }

        if (IsLoggingDraftDirty())
        {
            nextMask |= 1 << (int)Tab.Logging;
        }

        if (IsMsfxApiDirty())
        {
            nextMask |= 1 << (int)Tab.MsfxApi;
        }

        if (HasPendingChanges)
        {
            nextMask |= 1 << (int)Tab.Agents;
        }

        if (nextMask == _unsavedMask)
        {
            return;
        }

        _unsavedMask = nextMask;
        UnsavedChanged?.Invoke();
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private bool IsDatabaseDirty()
    {
        var applied = _settings.AppliedDb;
        return !string.Equals(Host, applied.Host, StringComparison.Ordinal)
               || Port != applied.Port
               || !string.Equals(Database, applied.Database, StringComparison.Ordinal)
               || !string.Equals(Username, applied.Username, StringComparison.Ordinal)
               || !string.Equals(Password, applied.Password, StringComparison.Ordinal);
    }

    private bool IsClientAliasesDirty()
    {
        if (!IsClientAliasEditMode || _clientAliasEditBaseline is null)
        {
            return false;
        }

        return !AliasMapsEqual(_clientAliasEditBaseline, BuildAliasMapFromRows());
    }

    private bool IsTraceCodeRuleDirty()
    {
        var rule = _traceCodeRule.Current;
        return TraceCodeRequiredLength != rule.RequiredLength
               || !string.Equals(TraceCodePattern, rule.Pattern, StringComparison.Ordinal);
    }

    private bool IsUpdateDraftDirty()
    {
        var options = _updateSettings.Current;
        return !string.Equals(UpdateFeedUrl ?? string.Empty, options.FeedUrl ?? string.Empty, StringComparison.Ordinal)
               || UpdatePollIntervalMinutes != options.AutoCheckIntervalMinutes
               || !string.Equals(IgnoredProductVersion ?? string.Empty, options.IgnoredVersion ?? string.Empty, StringComparison.Ordinal);
    }

    private bool IsLoggingDraftDirty()
    {
        var options = _loggingSettings.Current;
        return LoggingRetentionDays != options.RetentionDays
               || LoggingMaxFileSizeMb != options.MaxFileSizeMb
               || !string.Equals(LoggingDirectory ?? string.Empty, _logger.LogDirectory, StringComparison.Ordinal);
    }

    private bool IsMsfxApiDirty()
    {
        var saved = _appConfigStore.Load().MsfxApi ?? new MsfxApiOptions();
        var draft = BuildMsfxOptionsFromUi();
        return !MsfxOptionsEqual(saved, draft);
    }

    private static bool MsfxOptionsEqual(MsfxApiOptions left, MsfxApiOptions right)
        => string.Equals(left.GatewayUrl, right.GatewayUrl, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.AppKey, right.AppKey, StringComparison.Ordinal)
           && string.Equals(left.AppSecret, right.AppSecret, StringComparison.Ordinal)
           && string.Equals(left.SessionToken, right.SessionToken, StringComparison.Ordinal)
           && string.Equals(left.RefEntId, right.RefEntId, StringComparison.Ordinal)
           && left.TimeoutSeconds == right.TimeoutSeconds;

    private Dictionary<string, string> BuildAliasMapFromRows()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ClientAliases)
        {
            var machine = (row.Machine ?? string.Empty).Trim();
            if (machine.Length == 0)
            {
                continue;
            }

            map[machine] = (row.Alias ?? string.Empty).Trim();
        }

        return map;
    }

    private static bool AliasMapsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (machine, alias) in left)
        {
            if (!right.TryGetValue(machine, out var other)
                || !string.Equals(alias, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void RestoreDatabaseFields(PgOptions options)
    {
        Host = options.Host;
        Port = options.Port;
        Database = options.Database;
        Username = options.Username;
        Password = options.Password;
        OnPropertyChanged(nameof(Port));
    }

    private void CaptureClientAliasEditBaseline()
        => _clientAliasEditBaseline = BuildAliasMapFromRows();

    private void ScheduleLoggingAutoSave()
    {
        if (_syncingLoggingOptions)
        {
            return;
        }

        _loggingAutoSaveCts?.Cancel();
        _loggingAutoSaveCts?.Dispose();
        _loggingAutoSaveCts = new CancellationTokenSource();
        var token = _loggingAutoSaveCts.Token;
        TaskObserve.Observe(ApplyLoggingAfterDelayAsync(token), "SettingsVM", "logging.autosave.fail");
    }

    private async Task ApplyLoggingAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        if (!_disposed)
        {
            while (IsLoggingBusy)
            {
                try
                {
                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }

            await ApplyLoggingOptionsAsync(silent: true);
        }
    }

    private void ScheduleAutoCheckSave()
    {
        if (_syncingUpdateOptions)
        {
            return;
        }

        _updateAutoCheckSaveCts?.Cancel();
        _updateAutoCheckSaveCts?.Dispose();
        _updateAutoCheckSaveCts = new CancellationTokenSource();
        var token = _updateAutoCheckSaveCts.Token;
        TaskObserve.Observe(ApplyAutoCheckAfterDelayAsync(token), "SettingsVM", "update.autosave.fail");
    }

    private async Task ApplyAutoCheckAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }

        if (!_disposed)
        {
            await ApplyAutoCheckImmediateAsync();
        }
    }

    private async Task ApplyAutoCheckImmediateAsync()
    {
        if (_syncingUpdateOptions)
        {
            return;
        }

        try
        {
            var previous = _updateSettings.Current;
            var options = AppUpdatePolicy.NormalizeOptions(previous);
            options.AutoCheckOnStartup = AutoCheckUpdateOnStartup;

            await SaveUpdateOptionsLocalAsync(options);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.autosave.fail", "Failed to auto-save update preferences", ex);
            await RunOnUiAsync(() =>
            {
                _syncingUpdateOptions = true;
                AutoCheckUpdateOnStartup = _updateSettings.Current.AutoCheckOnStartup;
                _syncingUpdateOptions = false;
                _toast.Error("更新设置", $"自动保存失败：{ex.Message}");
            });
        }
        finally
        {
            await RunOnUiAsync(RefreshUnsaved);
        }
    }

    partial void OnHostChanged(string value) => RefreshUnsaved();
    partial void OnPortChanged(int value) => RefreshUnsaved();
    partial void OnDatabaseChanged(string value) => RefreshUnsaved();
    partial void OnUsernameChanged(string value) => RefreshUnsaved();
    partial void OnPasswordChanged(string value) => RefreshUnsaved();
    partial void OnTraceCodeRequiredLengthChanged(int value) => RefreshUnsaved();
    partial void OnTraceCodePatternChanged(string value) => RefreshUnsaved();
    partial void OnUpdateChannelChanged(string value)
    {
        SyncPollHint();
        if (!_syncingUpdateOptions)
        {
            _updateChannelSaveCts?.Cancel();
            _updateChannelSaveCts?.Dispose();
            _updateChannelSaveCts = new CancellationTokenSource();
            TaskObserve.Observe(
                ApplyUpdateChannelImmediateAsync(value, _updateChannelSaveCts.Token),
                "SettingsVM",
                "update.channel.save.fail");
        }
    }

    partial void OnUpdateFeedUrlChanged(string value) => RefreshUnsaved();
    partial void OnIgnoredProductVersionChanged(string value) => RefreshUnsaved();
    partial void OnMsfxGatewayUrlChanged(string value) => RefreshUnsaved();
    partial void OnMsfxAppKeyChanged(string value) => RefreshUnsaved();
    partial void OnMsfxAppSecretChanged(string value) => RefreshUnsaved();
    partial void OnMsfxSessionTokenChanged(string value) => RefreshUnsaved();
    partial void OnMsfxRefEntIdChanged(string value) => RefreshUnsaved();
    partial void OnMsfxTimeoutSecondsChanged(int value) => RefreshUnsaved();

    partial void OnAutoCheckUpdateOnStartupChanged(bool value)
    {
        ScheduleAutoCheckSave();
    }

    partial void OnUpdatePollIntervalMinutesChanged(int value)
    {
        SyncPollHint();
        RefreshUnsaved();
    }

    partial void OnLoggingEnabledChanged(bool value)
    {
        if (!_syncingLoggingOptions)
        {
            ScheduleLoggingAutoSave();
        }
    }

    partial void OnLoggingMinimumLevelChanged(string value)
    {
        OnPropertyChanged(nameof(LoggingMinimumLevelHint));
        if (!_syncingLoggingOptions)
        {
            ScheduleLoggingAutoSave();
        }
    }

    partial void OnLoggingRetentionDaysChanged(int value)
    {
        if (!_syncingLoggingOptions)
        {
            RefreshUnsaved();
        }
    }

    partial void OnLoggingMaxFileSizeMbChanged(int value)
    {
        if (!_syncingLoggingOptions)
        {
            RefreshUnsaved();
        }
    }

    partial void OnLoggingDirectoryChanged(string value)
    {
        if (!_syncingLoggingOptions)
        {
            RefreshUnsaved();
        }
    }

    partial void OnIsClientAliasEditModeChanged(bool value)
    {
        if (value)
        {
            CaptureClientAliasEditBaseline();
        }
        else
        {
            _clientAliasEditBaseline = null;
        }

        RefreshUnsaved();
    }
}
