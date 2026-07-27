using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 设置页未保存变更门禁；离开页前须先保存或丢弃
/// </summary>
public interface ISettingsPage
{
    bool HasUnsavedChanges { get; }
    Task<bool> TrySaveOrDiscardAllAsync();
}

/// <summary>
/// 设置页 ViewModel
///
/// 负责：
/// - DB / Agents / MSFX / 更新 / 别名等配置表单
/// - 未保存变更门禁与热应用边界
/// </summary>
public partial class Settings : AppPageBase, ISettingsPage
{
    public override string Icon => "Settings";
    public override int Index => 999;
    public override string DisplayName => "设置";
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;
    protected override bool SupportsStaleWhileReconnect => false;

    private readonly ISettingsService _settings;
    private readonly IToastService _toast;
    private readonly IClientAliasService _alias;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IUiBehaviorService _uiBehavior;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateFlowService _updateFlow;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IDialogService _dialog;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IAppLogger _logger;
    private readonly IClipboardService _clipboard;
    private readonly ISyncService _msfxSync;
    private readonly HashSet<ClientAliasRow> _trackedAliasRows = new();
    private CancellationTokenSource _pageWorkCts = new();
    private bool _disposed;
    private bool _pageWorkCancelled;
    private bool _syncingUiBehavior;
    private bool _syncingUpdateOptions;
    private int _localUpdateSaveCount;
    private bool _syncingLoggingOptions;
    private int _localLoggingSaveCount;
    private int _localClientAliasSaveCount;
    private const string MsfxDefaultGatewayUrl = "https://eco.taobao.com/router/rest";
    private const string MsfxPullSourceApi = "listupout";

    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _database;
    [ObservableProperty] private string _username;
    [ObservableProperty] private string _password;

    [ObservableProperty] private bool _isDbConnected;
    [ObservableProperty] private bool _isClientAliasRefreshing;
    [ObservableProperty] private int _traceCodeRequiredLength = 20;
    [ObservableProperty] private string _traceCodePattern = "^8\\d+$";
    [ObservableProperty] private bool _minimizeToTrayOnClose = true;
    [ObservableProperty] private bool _autoCheckUpdateOnStartup = true;
    [ObservableProperty] private string _updateChannel = "stable";
    [ObservableProperty] private string _updateFeedUrl = string.Empty;
    [ObservableProperty] private int _updatePollIntervalMinutes;
    [ObservableProperty] private string _updatePollIntervalHint = "0=通道默认";
    [ObservableProperty] private string _ignoredProductVersion = string.Empty;
    [ObservableProperty] private string _currentProductVersion = "unknown";
    [ObservableProperty] private bool? _updateAvailability;
    [ObservableProperty] private string _latestProductVersion = "unknown";
    [ObservableProperty] private string _updateChannelSwitchHint = "检查具体更新版本时会验证目标 Feed 与数据库兼容范围";
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private bool _hasUpdateTarget;
    [ObservableProperty] private bool _hasCheckedUpdate;
    [ObservableProperty] private bool _hasDownloadedUpdate;
    [ObservableProperty] private string _updateTargetVersion = "--";
    [ObservableProperty] private string _updateTargetSource = "--";
    [ObservableProperty] private string _updateTargetDatabase = "--";
    [ObservableProperty] private string _updateTargetSchemaRange = "--";
    [ObservableProperty] private string _updateTargetCheckedAt = "--";
    [ObservableProperty] private string _updateTargetDownloadedAt = "--";
    [ObservableProperty] private string _updateTargetStatus = "--";
    [ObservableProperty] private string _updateTargetMessage = "--";
    [ObservableProperty] private bool _loggingEnabled = true;
    [ObservableProperty] private string _loggingMinimumLevel = "Error";
    [ObservableProperty] private int _loggingRetentionDays = 14;
    [ObservableProperty] private int _loggingMaxFileSizeMb = 20;
    [ObservableProperty] private string _loggingDirectory = string.Empty;
    [ObservableProperty] private bool _isLoggingBusy;
    [ObservableProperty] private string _dbSchemaCurrentVersion = "unknown";
    [ObservableProperty] private string _dbSchemaTargetVersion = "unknown";
    [ObservableProperty] private string _dbSchemaRequiredMinVersion = "unknown";
    [ObservableProperty] private string _dbSchemaRequiredMaxVersion = "unknown";
    [ObservableProperty] private string _dbSchemaStatusText = "未检查";
    [ObservableProperty] private bool _isDbSchemaChecking;
    [ObservableProperty] private string _dbSchemaErrorText = string.Empty;
    [ObservableProperty] private bool? _dbSchemaBadgeStatus;
    [ObservableProperty] private string _dbSchemaBadgeLabel = "未检查";
    [ObservableProperty] private string _dbSchemaLastCheckedAtText = "--";
    [ObservableProperty] private string _dbSchemaLastCheckSourceText = "--";
    public string DbSchemaManagementText
        => "仅检查数据库兼容性；请使用服务器端数据库部署工具更新结构";
    [ObservableProperty] private string _msfxGatewayUrl = "https://eco.taobao.com/router/rest";
    [ObservableProperty] private string _msfxAppKey = string.Empty;
    [ObservableProperty] private string _msfxAppSecret = string.Empty;
    [ObservableProperty] private string _msfxSessionToken = string.Empty;
    [ObservableProperty] private string _msfxRefEntId = string.Empty;
    [ObservableProperty] private int _msfxTimeoutSeconds = 20;
    [ObservableProperty] private bool? _msfxApiBadgeStatus;
    [ObservableProperty] private string _msfxApiBadgeLabel = "未配置";
    [ObservableProperty] private bool _isMsfxCursorBusy;
    [ObservableProperty] private DateTime? _msfxCursorTargetDate = DateTime.Today;
    [ObservableProperty] private string _msfxCursorCurrentText = "未读取";
    public DateTime MsfxCursorMaxDate => DateTime.Today;

    [ObservableProperty] private bool _isClientAliasEditMode;
    [ObservableProperty] private bool _isClientAliasReadOnly = true;
    public bool CanCopyDbSchemaDiagnostics => !string.IsNullOrWhiteSpace(BuildDbSchemaDiagnosticsText());
    public ObservableCollection<string> LoggingLevelOptions { get; } = new()
    {
        "Debug",
        "Info",
        "Warn",
        "Error",
        "Fatal"
    };
    public ObservableCollection<string> UpdateChannelOptions { get; } = new()
    {
        "stable",
        "beta"
    };

    public ObservableCollection<ClientAliasRow> ClientAliases { get; } = new();
    public bool IsClientAliasesEmpty => ClientAliases.Count == 0;
    public string UpdateAvailabilityLabel => GetAvailabilityLabel(UpdateAvailability);
    public bool IsUpdateApplying => _updateFlow.IsApplying;
    public bool CanApplyProductUpdateNow => HasUpdateAvailable
        && !IsUpdateChecking
        && !IsUpdateApplying;
    public string LoggingMinimumLevelHint => LoggingMinimumLevel switch
    {
        "Debug" => "记录最详细调试信息，适合临时排障",
        "Info" => "记录关键流程信息，便于常规回溯",
        "Warn" => "仅记录异常征兆与潜在问题",
        "Error" => "仅记录错误与失败，推荐日常运行",
        "Fatal" => "仅记录致命故障，最小日志开销",
        _ => "日志级别未识别，将使用 Error"
    };
    public Settings(
        IAppConfigStore appConfigStore,
        ISettingsService settings,
        IToastService toast,
        IClientAliasService alias,
        ITraceCodeRuleService traceCodeRule,
        IUiBehaviorService uiBehavior,
        IUpdateSettingsService updateSettings,
        IAppUpdateService updates,
        IUpdateFlowService updateFlow,
        IReleaseVersionService releaseVersion,
        IDialogService dialog,
        ILoggingSettingsService loggingSettings,
        IAppLogger logger,
        IClipboardService clipboard,
        ISyncService msfxSync,
        IAgentsRuntime agents,
        IAgentsConfigService agentsConfig,
        IModuleSettingsStore moduleSettings)
    {
        _appConfigStore = appConfigStore;
        _settings = settings;
        _toast = toast;
        _alias = alias;
        _traceCodeRule = traceCodeRule;
        _uiBehavior = uiBehavior;
        _updateSettings = updateSettings;
        _updates = updates;
        _updateFlow = updateFlow;
        _releaseVersion = releaseVersion;
        _dialog = dialog;
        _loggingSettings = loggingSettings;
        _logger = logger;
        _clipboard = clipboard;
        _msfxSync = msfxSync;
        _agents = agents;
        _agentsConfig = agentsConfig;
        _moduleSettings = moduleSettings ?? throw new ArgumentNullException(nameof(moduleSettings));
        InitializeAgents();
        ClientAliases.CollectionChanged += OnClientAliasesChanged;
        var c = settings.AppliedDb;
        _host = c.Host;
        _port = c.Port;
        _database = c.Database;
        _username = c.Username;
        _password = c.Password;

        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        SyncTraceCodeRule();
        SyncMsfxApi();
        SyncUiBehavior();
        SyncUpdateOptions();
        SyncLogging();
        RunDetached(RefreshSchemaStatusOnStartupAsync, "db.schema.startup_refresh.fire_and_forget_fail");
        _uiBehavior.Changed += OnUiBehaviorChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;
        _updates.Changed += OnUpdatesChanged;
        _updateFlow.StateChanged += OnUpdateFlowStateChanged;
        _loggingSettings.Changed += OnLoggingSettingsChanged;
        _alias.Changed += OnClientAliasMapChanged;
        _traceCodeRule.Changed += OnTraceCodeRuleChanged;

    }

    private void SyncMsfxApi()
    {
        var options = _appConfigStore.Load().MsfxApi ?? new MsfxApiOptions();
        MsfxGatewayUrl = string.Equals(options.GatewayUrl, MsfxDefaultGatewayUrl, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : options.GatewayUrl;
        MsfxAppKey = options.AppKey;
        MsfxAppSecret = options.AppSecret;
        MsfxSessionToken = options.SessionToken;
        MsfxRefEntId = options.RefEntId;
        MsfxTimeoutSeconds = options.TimeoutSeconds;
        RefreshMsfxBadge(options);
    }

    public Task RefreshSchemaStatusAsync(string source = "startup_postcheck")
        => UpdateSchemaStatusAsync(source, manualProbe: false, bindPageLifetime: false);

    private void OnClientAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsClientAliasesEmpty));

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        SyncPageAvailability();
        _pageWorkCancelled = false;
        ReloadAgentsRuntime();
        RefreshUnsaved();
        ReloadClientAliasesIfVisible("client_alias.reload.activate_fail");
        // MSFX cursor 落在 DB；断连提示由 shell 统一发，DB 不可用时跳过
        if (CanPageFromDb)
        {
            RunDetached(RefreshMsfxCursorCoreAsync, "msfx.cursor.refresh.activate_fail");
        }

        return Task.CompletedTask;
    }

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelPageWork();
        return Task.CompletedTask;
    }

    private void RunDetached(Func<CancellationToken, Task> work, string eventName)
    {
        var token = _pageWorkCts.Token;
        TaskObserve.Observe(RunDetachedAsync(work, eventName, token), "SettingsVM", eventName);
    }

    private async Task RunDetachedAsync(Func<CancellationToken, Task> work, string eventName, CancellationToken ct)
    {
        try
        {
            if (_disposed)
            {
                return;
            }

            await work(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!CanToastError(ex))
            {
                _logger.Warn("SettingsVM", eventName, "Settings background operation skipped toast (DB unavailable)", ex);
                return;
            }

            _logger.Error("SettingsVM", eventName, "Settings background operation failed", ex);
            await RunOnUiAsync(() => _toast.Error("设置后台任务失败", ex.Message));
        }
    }

    private CancellationTokenSource CreatePageOperationCts(TimeSpan? timeout = null)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_pageWorkCts.Token);
        if (timeout.HasValue)
        {
            cts.CancelAfter(timeout.Value);
        }

        return cts;
    }

    private bool IsPageWorkCancellation()
        => _disposed || _pageWorkCancelled;

    private Task SetAliasRefreshingAsync(bool value)
        => RunOnUiAsync(() => IsClientAliasRefreshing = value);

    private Task ShowErrorAsync(string title, string message)
        => RunOnUiAsync(() => _toast.Error(title, message));

    private void PostUi(Action action, string eventName)
    {
        TaskObserve.Observe(PostUiAsync(action, eventName), "SettingsVM", eventName);
    }

    private async Task PostUiAsync(Action action, string eventName)
    {
        try
        {
            await RunOnUiAsync(action);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", eventName, "Settings desktop continuation failed", ex);
        }
    }

    private void CancelPageWork()
    {
        _pageWorkCancelled = true;
        var old = Interlocked.Exchange(ref _pageWorkCts, new CancellationTokenSource());
        try { old.Cancel(); }
        catch (ObjectDisposedException) { }
        old.Dispose();
    }

    private void SyncUiBehavior()
    {
        var ui = _uiBehavior.Current;
        MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
    }

    private void SyncUpdateOptions()
    {
        _syncingUpdateOptions = true;

        var options = _updateSettings.Current;
        AutoCheckUpdateOnStartup = options.AutoCheckOnStartup;
        UpdateChannel = options.Channel;
        UpdateFeedUrl = options.FeedUrl;
        UpdatePollIntervalMinutes = options.AutoCheckIntervalMinutes;
        IgnoredProductVersion = options.IgnoredVersion;
        SyncPollHint();

        SyncUpdateState();

        _syncingUpdateOptions = false;
    }

    private void OnUiBehaviorChanged()
    {
        PostUi(() =>
        {
            var ui = _uiBehavior.Current;
            _syncingUiBehavior = true;
            MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
            _syncingUiBehavior = false;
        }, "desktop_behavior.changed.fail");
    }

    private void OnUpdateSettingsChanged()
    {
        var isLocalSave = Volatile.Read(ref _localUpdateSaveCount) > 0;
        PostUi(() =>
        {
            if (isLocalSave)
            {
                RefreshUnsaved();
                return;
            }

            if (IsTabDirty((int)Tab.Updates))
            {
                _toast.Warn("应用更新", "配置文件已更新，当前未保存的更新设置未同步");
                return;
            }

            SyncUpdateOptions();
        }, "update_settings.changed.ui_fail");
    }

    private void OnUpdatesChanged()
    {
        PostUi(SyncUpdateState, "updates.changed.ui_fail");
    }

    private void SyncLogging()
    {
        _syncingLoggingOptions = true;
        var options = _loggingSettings.Current;
        LoggingEnabled = options.Enabled;
        LoggingMinimumLevel = options.MinimumLevel;
        LoggingRetentionDays = options.RetentionDays;
        LoggingMaxFileSizeMb = options.MaxFileSizeMb;
        LoggingDirectory = _logger.LogDirectory;
        _syncingLoggingOptions = false;
    }

    private void OnLoggingSettingsChanged()
    {
        var isLocalSave = Volatile.Read(ref _localLoggingSaveCount) > 0;
        PostUi(() =>
        {
            if (isLocalSave)
            {
                RefreshUnsaved();
                return;
            }

            if (IsTabDirty((int)Tab.Logging))
            {
                _toast.Warn("日志设置", "配置文件已更新，当前未保存的日志输入未同步");
                return;
            }

            SyncLogging();
        }, "logging_settings.changed.fail");
    }

    private void OnClientAliasMapChanged()
    {
        var isLocalSave = Volatile.Read(ref _localClientAliasSaveCount) > 0;
        PostUi(() =>
        {
            if (isLocalSave)
            {
                return;
            }

            if (IsClientAliasEditMode)
            {
                _toast.Warn(
                    "客户端别名",
                    "配置文件中的别名已更新；当前编辑未同步，保存将覆盖外部修改，或放弃编辑以加载最新");
                return;
            }

            if (_activeTabIndex == (int)Tab.ClientAliases)
            {
                RebindClientAliasRowsFromMap();
            }
        }, "client_alias.changed.ui_fail");
    }

    private void OnTraceCodeRuleChanged()
    {
        PostUi(() =>
        {
            if (IsTabDirty((int)Tab.TraceCodeRule))
            {
                _toast.Warn("追溯码规则", "配置文件已更新，当前未保存的规则未同步");
                return;
            }

            SyncTraceCodeRule();
            RefreshUnsaved();
        }, "trace_rule.changed.ui_fail");
    }

    partial void OnUpdateAvailabilityChanged(bool? value)
    {
        OnPropertyChanged(nameof(UpdateAvailabilityLabel));
        OnPropertyChanged(nameof(CanApplyProductUpdateNow));
    }

    partial void OnHasUpdateAvailableChanged(bool value)
        => OnPropertyChanged(nameof(CanApplyProductUpdateNow));

    partial void OnIsUpdateCheckingChanged(bool value)
        => OnPropertyChanged(nameof(CanApplyProductUpdateNow));

    private void OnUpdateFlowStateChanged()
        => PostUi(() =>
        {
            OnPropertyChanged(nameof(IsUpdateApplying));
            OnPropertyChanged(nameof(CanApplyProductUpdateNow));
        }, "update_flow.state.ui_fail");

    private void SyncUpdateState()
    {
        CurrentProductVersion = _updates.CurrentVersion;
        LatestProductVersion = _updates.LatestVersion;
        UpdateAvailability = _updates.UpdateAvailability;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;
        var target = _updates.Target;
        HasUpdateTarget = target is not null;
        HasCheckedUpdate = target?.CheckedAt is not null;
        HasDownloadedUpdate = target?.DownloadedAt is not null;
        UpdateTargetVersion = target?.Version ?? "--";
        UpdateTargetSource = target is null ? "--" : $"{target.Channel} · {target.FeedUrl}";
        UpdateTargetDatabase = target?.DatabaseLabel ?? "--";
        UpdateTargetSchemaRange = target is null
            ? "--"
            : $"{target.RequiredMinDbSchema} - {target.RequiredMaxDbSchema}";
        UpdateTargetCheckedAt = target?.CheckedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        UpdateTargetDownloadedAt = target?.DownloadedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
        UpdateTargetStatus = target is null ? "--" : GetUpdateTargetStatus(target.Stage);
        UpdateTargetMessage = target?.Message ?? "--";
    }

    private static string GetUpdateTargetStatus(UpdateTargetStage stage) => stage switch
    {
        UpdateTargetStage.Available => "可更新",
        UpdateTargetStage.Downloading => "正在更新",
        UpdateTargetStage.ReadyToInstall => "等待安装",
        UpdateTargetStage.Blocked => "无法安装",
        _ => "未知"
    };

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (_syncingUiBehavior)
        {
            return;
        }

        RunDetached(ct => SaveUiBehaviorImmediateAsync(value, ct), "desktop_behavior.save.fire_and_forget_fail");
    }

    private void SyncPollHint()
    {
        var channel = (UpdateChannel ?? string.Empty).Trim();
        var defaultMinutes = string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) ? 30 : 10;

        if (UpdatePollIntervalMinutes <= 0)
        {
            UpdatePollIntervalHint = $"默认：{channel} {defaultMinutes} 分钟";
        }
        else
        {
            UpdatePollIntervalHint = $"自定义：{Math.Clamp(UpdatePollIntervalMinutes, 1, 720)} 分钟";
        }
    }

    private void SyncTraceCodeRule()
    {
        var rule = _traceCodeRule.Current;
        TraceCodeRequiredLength = rule.RequiredLength;
        TraceCodePattern = rule.Pattern;
    }

    public override void Dispose()
    {
        _disposed = true;
        _pageWorkCancelled = true;
        _loggingAutoSaveCts?.Cancel();
        _loggingAutoSaveCts?.Dispose();
        _loggingAutoSaveCts = null;
        _updateAutoCheckSaveCts?.Cancel();
        _updateAutoCheckSaveCts?.Dispose();
        _updateAutoCheckSaveCts = null;
        _updateChannelSaveCts?.Cancel();
        _updateChannelSaveCts?.Dispose();
        _updateChannelSaveCts = null;
        CancelPageWork();
        try { _uiBehavior.Changed -= OnUiBehaviorChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.desktop_behavior_unsub_fail", "Failed to unsubscribe UiBehavior", ex);
        }
        try { _updateSettings.Changed -= OnUpdateSettingsChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.update_settings_unsub_fail", "Failed to unsubscribe UpdateSettings", ex);
        }
        try { _updates.Changed -= OnUpdatesChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.updates_unsub_fail", "Failed to unsubscribe Updates", ex);
        }
        try { _updateFlow.StateChanged -= OnUpdateFlowStateChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.update_flow_unsub_fail", "Failed to unsubscribe UpdateFlow", ex);
        }
        try { _loggingSettings.Changed -= OnLoggingSettingsChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.logging_settings_unsub_fail", "Failed to unsubscribe LoggingSettings", ex);
        }
        try { _alias.Changed -= OnClientAliasMapChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.client_alias_unsub_fail", "Failed to unsubscribe ClientAlias", ex);
        }
        try { _traceCodeRule.Changed -= OnTraceCodeRuleChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.trace_rule_unsub_fail", "Failed to unsubscribe TraceCodeRule", ex);
        }
        ClientAliases.CollectionChanged -= OnClientAliasesChanged;
        DisposeAgents();
        _pageWorkCts.Dispose();
        base.Dispose();
    }
}
