using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class SettingsViewModel
{
    private enum Tab
    {
        Database = 0,
        ClientAliases = 1,
        TraceCodeRule = 2,
        UiBehavior = 3,
        Updates = 4,
        Logging = 5,
        MsfxApi = 6
    }

    private static readonly string[] TabTitles =
    [
        "PostgreSQL 设置",
        "客户端别名映射",
        "追溯码校验规则",
        "界面行为",
        "应用更新",
        "日志与诊断",
        "码上放心 API"
    ];

    private int _unsavedMask;
    private Dictionary<string, string>? _clientAliasEditBaseline;
    private CancellationTokenSource? _loggingAutoSaveCts;
    private CancellationTokenSource? _updateAutoSaveCts;

    public event Action? UnsavedChanged;

    public bool HasUnsavedChanges => _unsavedMask != 0;

    public bool IsTabDirty(int tabIndex)
        => tabIndex >= 0
           && tabIndex < TabTitles.Length
           && (_unsavedMask & (1 << tabIndex)) != 0;

    public async Task<bool> ConfirmLeaveTabAsync(int tabIndex)
    {
        if (!IsTabDirty(tabIndex))
        {
            return true;
        }

        var choice = await _dialog.Confirm3(
            "有未保存的更改",
            $"「{TabTitles[tabIndex]}」中的修改尚未保存。",
            "保存并继续",
            "放弃更改",
            "取消");

        if (choice == 1)
        {
            return await SaveTabAsync((Tab)tabIndex);
        }

        if (choice == 2)
        {
            RevertTab((Tab)tabIndex);
            return true;
        }

        return false;
    }

    public async Task<bool> TrySaveOrDiscardAllAsync()
    {
        RefreshUnsaved();
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var names = string.Join("、", GetDirtyTabTitles());
        var choice = await _dialog.Confirm3(
            "设置页有未保存的更改",
            $"以下板块尚未保存：{names}",
            "全部保存",
            "放弃全部",
            "取消");

        switch (choice)
        {
            case 1:
                foreach (var tab in GetDirtyTabs().ToArray())
                {
                    if (!await SaveTabAsync(tab))
                    {
                        return false;
                    }
                }

                return true;
            case 2:
                foreach (var tab in GetDirtyTabs().ToArray())
                {
                    RevertTab(tab);
                }

                return true;
            default:
                return false;
        }
    }

    private async Task<bool> SaveTabAsync(Tab tab)
        => tab switch
        {
            Tab.Database => await ApplyDbConfigAsync(),
            Tab.ClientAliases => await ApplyClientAliasesAsync(),
            Tab.TraceCodeRule => await ApplyTraceCodeRuleAsync(),
            Tab.Updates => await ApplyUpdateOptionsAsync(),
            Tab.MsfxApi => await ApplyMsfxApiConfigAsync(),
            Tab.UiBehavior or Tab.Logging => true,
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
                LoadAliasesOnly();
                RefreshClientAlias();
                break;
            case Tab.TraceCodeRule:
                LoadTraceCodeRule();
                break;
            case Tab.Updates:
                LoadUpdateOptions();
                break;
            case Tab.MsfxApi:
                LoadMsfxApiOptions();
                break;
            case Tab.UiBehavior:
                LoadUiBehavior();
                break;
            case Tab.Logging:
                LoadLoggingOptions();
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

        if (IsMsfxApiDirty())
        {
            nextMask |= 1 << (int)Tab.MsfxApi;
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
        return !string.Equals(NormalizeUpdateChannel(UpdateChannel), options.Channel, StringComparison.Ordinal)
               || !string.Equals(UpdateFeedUrl ?? string.Empty, options.FeedUrl ?? string.Empty, StringComparison.Ordinal)
               || !string.Equals(IgnoredProductVersion ?? string.Empty, options.IgnoredVersion ?? string.Empty, StringComparison.Ordinal);
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
        RunDetached(async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            try
            {
                await Task.Delay(400, linked.Token);
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
                return;
            }

            await ApplyLoggingOptionsAsync(silent: true);
        }, "logging.autosave.fail");
    }

    private void ScheduleUpdateAutoSave()
    {
        if (_syncingUpdateOptions)
        {
            return;
        }

        _updateAutoSaveCts?.Cancel();
        _updateAutoSaveCts?.Dispose();
        _updateAutoSaveCts = new CancellationTokenSource();
        var token = _updateAutoSaveCts.Token;
        RunDetached(async ct =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
            try
            {
                await Task.Delay(400, linked.Token);
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
                return;
            }

            await ApplyUpdateAutoFieldsAsync();
        }, "update.autosave.fail");
    }

    private async Task ApplyUpdateAutoFieldsAsync()
    {
        if (_syncingUpdateOptions || SkipTrigger())
        {
            return;
        }

        try
        {
            var previous = _updateSettings.Current;
            var options = new UpdateOptions
            {
                AutoCheckOnStartup = AutoCheckUpdateOnStartup,
                Channel = previous.Channel,
                ValidatedChannel = previous.ValidatedChannel,
                FeedUrl = previous.FeedUrl,
                AutoCheckIntervalMinutes = Math.Clamp(UpdatePollIntervalMinutes, 0, 720),
                IgnoredVersion = previous.IgnoredVersion
            };

            await _updateSettings.SaveAsync(options);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.autosave.fail", "Failed to auto-save update preferences", ex);
            await RunOnUiAsync(() => _toast.Error("更新设置", $"自动保存失败：{ex.Message}"));
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
        RefreshUnsaved();
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
        ScheduleUpdateAutoSave();
    }

    partial void OnUpdatePollIntervalMinutesChanged(int value)
    {
        SyncPollHint();
        ScheduleUpdateAutoSave();
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
            ScheduleLoggingAutoSave();
        }
    }

    partial void OnLoggingMaxFileSizeMbChanged(int value)
    {
        if (!_syncingLoggingOptions)
        {
            ScheduleLoggingAutoSave();
        }
    }

    partial void OnLoggingDirectoryChanged(string value)
    {
        if (!_syncingLoggingOptions)
        {
            ScheduleLoggingAutoSave();
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
