using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;
using PacToolkits.Desktop.Avalonia.Ui.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public partial class Settings
{
    private enum Tab
    {
        Connection = 0,
        ClientAliases = 1,
        TraceCodeRule = 2,
        UiBehavior = 3,
        Updates = 4,
        Logging = 5,
        MsfxApi = 6,
        Agents = 7,
        ModuleSettings = 8,
        BarcodeGen = 9
    }

    private static readonly string[] TabTitles =
    [
        "连接设置",
        "客户端别名映射",
        "追溯码校验规则",
        "界面行为",
        "应用更新",
        "日志与诊断",
        "码上放心 API",
        "自动化集成",
        "模块配置",
        "条码生成"
    ];

    private int _unsavedMask;
    private int _activeTabIndex = -1;
    private int? _pendingOpenTabIndex;
    private string? _pendingModuleSettingsId;
    private Dictionary<string, string>? _clientAliasEditBaseline;
    private CancellationTokenSource? _loggingAutoSaveCts;
    private CancellationTokenSource? _updateAutoCheckSaveCts;
    private CancellationTokenSource? _updateChannelSaveCts;

    public event Action? UnsavedChanged;

    /// <summary>请求设置视图切换到指定 Tab（由 view code-behind 处理离开确认）</summary>
    public event Action<int>? TabOpenRequested;

    public bool HasUnsavedChanges => _unsavedMask != 0;

    public bool IsTabDirty(int tabIndex)
        => tabIndex >= 0
           && tabIndex < TabTitles.Length
           && (_unsavedMask & (1 << tabIndex)) != 0;

    public void OpenAgentsTab()
        => RequestOpenTab((int)Tab.Agents);

    public void OpenConnectionTab()
        => RequestOpenTab((int)Tab.Connection);

    public void OpenModuleSettingsTab(string moduleId)
    {
        _pendingModuleSettingsId = moduleId;
        RequestOpenTab((int)Tab.ModuleSettings);
    }

    /// <summary>导航未就绪时保留目标 Tab，就绪后由 view 取出</summary>
    public bool TryTakePendingOpenTab(out int tabIndex)
    {
        if (_pendingOpenTabIndex is not int pending)
        {
            tabIndex = 0;
            return false;
        }

        _pendingOpenTabIndex = null;
        tabIndex = pending;
        return true;
    }

    /// <summary>离开确认被取消时丢弃 deep-link，避免误跳或选错模块</summary>
    public void CancelPendingOpen()
    {
        _pendingOpenTabIndex = null;
        _pendingModuleSettingsId = null;
    }

    public void OnTabEntered(int tabIndex)
    {
        _activeTabIndex = tabIndex;
        if (_pendingOpenTabIndex == tabIndex)
        {
            _pendingOpenTabIndex = null;
        }

        ReloadClientAliasesIfVisible("client_alias.reload.tab_enter_fail");
        if (tabIndex != (int)Tab.ModuleSettings)
        {
            return;
        }

        // disk/stale 对齐后 soft apply；仍 pending 且可重建时才整表 syncModules（内带 discard）
        ReloadModuleSettingsIfVisible();
        ApplyPendingModuleSelection();
        if (string.IsNullOrWhiteSpace(_pendingModuleSettingsId)
            || IsModuleSettingsDirty()
            || HasModuleAutoSaves
            || IsAgentsToggling)
        {
            return;
        }

        SyncAgentsConfig(syncHost: false, syncModules: true);
    }

    // discardIfMissing：完整重建后再找不到则作废 deep-link；否则编辑器未就绪时保留
    private void ApplyPendingModuleSelection(bool discardIfMissing = false)
    {
        if (string.IsNullOrWhiteSpace(_pendingModuleSettingsId))
        {
            return;
        }

        TryFlushStaleModuleEditors();
        var match = ModuleEditors.FirstOrDefault(editor =>
            string.Equals(editor.ModuleId, _pendingModuleSettingsId, StringComparison.Ordinal));
        if (match is null)
        {
            if (discardIfMissing)
            {
                _pendingModuleSettingsId = null;
            }

            return;
        }

        _pendingModuleSettingsId = null;
        SelectedModuleEditor = match;
    }

    private void RequestOpenTab(int tabIndex)
    {
        // 非模块 Tab 的 deep-link 不应保留上次模块选中
        if (tabIndex != (int)Tab.ModuleSettings)
        {
            _pendingModuleSettingsId = null;
        }

        _pendingOpenTabIndex = tabIndex;
        TabOpenRequested?.Invoke(tabIndex);
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
            Tab.Connection => await ApplyConnectionTabAsync(),
            Tab.ClientAliases => await ApplyClientAliasesAsync(),
            Tab.TraceCodeRule => await ApplyTraceCodeRuleAsync(),
            Tab.BarcodeGen => await ApplyBarcodeGenAsync(),
            Tab.Updates => await ApplyUpdateOptionsAsync(),
            Tab.MsfxApi => await ApplyMsfxApiConfigAsync(),
            Tab.Agents => await ApplyAgentsSettingsAsync(showSuccessToast: false),
            Tab.ModuleSettings => await ApplyAgentsSettingsAsync(showSuccessToast: false),
            Tab.Logging => await ApplyLoggingOptionsAsync(silent: false),
            Tab.UiBehavior => true,
            _ => true
        };

    private void RevertTab(Tab tab)
    {
        switch (tab)
        {
            case Tab.Connection:
                SyncPacApi();
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
            case Tab.BarcodeGen:
                SyncBarcodeGen();
                break;
            case Tab.Updates:
                SyncUpdateOptions();
                break;
            case Tab.MsfxApi:
                SyncMsfxApi();
                break;
            case Tab.Agents:
                SyncAgentsConfig(syncHost: true, syncModules: false);
                break;
            case Tab.ModuleSettings:
                CancelModuleAutoSaves();
                SyncAgentsConfig(syncHost: false, syncModules: true);
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
        if (IsPacApiDirty())
        {
            nextMask |= 1 << (int)Tab.Connection;
        }

        if (IsClientAliasesDirty())
        {
            nextMask |= 1 << (int)Tab.ClientAliases;
        }

        if (IsTraceCodeRuleDirty())
        {
            nextMask |= 1 << (int)Tab.TraceCodeRule;
        }

        if (IsBarcodeGenDirty())
        {
            nextMask |= 1 << (int)Tab.BarcodeGen;
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

        if (IsAgentsHostDirty())
        {
            nextMask |= 1 << (int)Tab.Agents;
        }

        if (IsModuleSettingsDirty())
        {
            nextMask |= 1 << (int)Tab.ModuleSettings;
        }

        if (nextMask == _unsavedMask)
        {
            return;
        }

        _unsavedMask = nextMask;
        UnsavedChanged?.Invoke();
        OnPropertyChanged(nameof(HasUnsavedChanges));
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

    private bool IsBarcodeGenDirty()
    {
        var saved = BarcodeGenSettingsService.Normalize(_barcodeGenSettings.Current);
        var draft = BuildBarcodeGenDraft();
        return saved.ExcludeRecentDays != draft.ExcludeRecentDays
               || saved.Image.WidthPx != draft.Image.WidthPx
               || saved.Image.QuietZoneModules != draft.Image.QuietZoneModules
               || saved.Image.Unit != draft.Image.Unit
               || saved.Export.FileNamePrefix != draft.Export.FileNamePrefix;
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
               || !string.Equals(
                   LoggingDirectory ?? string.Empty,
                   LogDirectory.Resolve(options.LogDirectory).BrowseDirectory,
                   StringComparison.Ordinal);
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
            // 等手动打开/导出结束再写；IsLoggingBusy 不再被静默保存占用
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

    partial void OnPacApiUrlChanged(string value) => RefreshUnsaved();
    partial void OnPacApiKeyChanged(string value) => RefreshUnsaved();
    partial void OnPacApiAgentsKeyChanged(string value) => RefreshUnsaved();
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
