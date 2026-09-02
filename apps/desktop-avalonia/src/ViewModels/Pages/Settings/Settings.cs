using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Platform;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Update;
using PacToolkits.Desktop.Avalonia.Ui.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 定义设置页离开前处理未保存更改的契约
/// </summary>
public interface ISettingsPage
{
    bool HasUnsavedChanges { get; }
    Task<bool> TrySaveOrDiscardAllAsync();
}

/// <summary>
/// 设置页：PacAPI、Agents、MSFX、更新、客户端别名；区分保存与立即生效
/// </summary>
public partial class Settings : AppPageBase, ISettingsPage
{
    public override string Icon => "Settings";
    public override int Index => 999;
    public override string DisplayName => "设置";
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;
    protected override bool SupportsStaleWhileReconnect => false;

    private readonly IToastService _toast;
    private readonly IClientAliasService _alias;
    private readonly ILookupCatalogService _lookup;
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
    private readonly PacApiClient _pacApi;
    private readonly IApiAvailabilityService _apiAvailability;
    private readonly IPacApiContractGate _pacApiContractGate;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly ISensitiveUnlockService _unlockService;
    private PacApiOptions _pacApiBaseline = new();
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

    [ObservableProperty] private string _pacApiUrl = string.Empty;
    [ObservableProperty] private string _pacApiKey = string.Empty;
    [ObservableProperty] private string _pacApiAgentsKey = string.Empty;
    [ObservableProperty] private bool _isPacApiBusy;
    [ObservableProperty] private string _pacApiInfoLabel = "未探测";
    [ObservableProperty] private bool _isPacApiInfoUnknown = true;
    [ObservableProperty] private bool _isPacApiInfoReady;
    [ObservableProperty] private bool _isPacApiInfoFail;
    [ObservableProperty] private string _pacApiVersion = "--";
    [ObservableProperty] private string _pacApiContract = "--";
    [ObservableProperty] private string _pacApiContractRange = "--";
    [ObservableProperty] private string _pacApiDatabase = "--";
    [ObservableProperty] private string _pacApiSchema = "--";
    [ObservableProperty] private string _pacApiSchemaVersion = "--";
    [ObservableProperty] private string _pacApiCheckedAt = "--";
    [ObservableProperty] private string _pacApiDetail = "--";

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
    [ObservableProperty] private string _updateChannelSwitchHint = "检查更新时会确认目标通道 Feed 可用，并核对清单产品版本";
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private bool _hasUpdateTarget;
    [ObservableProperty] private bool _hasCheckedUpdate;
    [ObservableProperty] private bool _hasDownloadedUpdate;
    [ObservableProperty] private string _updateTargetVersion = "--";
    [ObservableProperty] private string _updateTargetSource = "--";
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
        IToastService toast,
        IClientAliasService alias,
        ILookupCatalogService lookup,
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
        PacApiClient pacApi,
        IApiAvailabilityService apiAvailability,
        IPacApiContractGate pacApiContractGate,
        IChangeWatermarkService changeWatermark,
        IBarcodeGenSettingsService barcodeGenSettings,
        ITraceBarcodeService traceBarcode,
        ISensitiveUnlockService unlockService,
        IAgentsRuntime agents,
        IAgentsConfigService agentsConfig,
        IModuleSettingsStore moduleSettings)
    {
        _appConfigStore = appConfigStore;
        _toast = toast;
        _alias = alias;
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
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
        _pacApi = pacApi ?? throw new ArgumentNullException(nameof(pacApi));
        _apiAvailability = apiAvailability ?? throw new ArgumentNullException(nameof(apiAvailability));
        _pacApiContractGate = pacApiContractGate ?? throw new ArgumentNullException(nameof(pacApiContractGate));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _barcodeGenSettings = barcodeGenSettings ?? throw new ArgumentNullException(nameof(barcodeGenSettings));
        _traceBarcode = traceBarcode ?? throw new ArgumentNullException(nameof(traceBarcode));
        _unlockService = unlockService ?? throw new ArgumentNullException(nameof(unlockService));
        _unlockStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _unlockStatusTimer.Tick += OnUnlockStatusTimerTick;
        _unlockService.StateChanged += OnUnlockChanged;
        _agents = agents;
        _agentsConfig = agentsConfig;
        _moduleSettings = moduleSettings ?? throw new ArgumentNullException(nameof(moduleSettings));
        InitializeAgents();
        ClientAliases.CollectionChanged += OnClientAliasesChanged;

        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        SyncTraceCodeRule();
        SyncBarcodeGen();
        SyncPacApi();
        SyncPacApiInfo();
        SyncMsfxApi();
        SyncUiBehavior();
        SyncUpdateOptions();
        SyncLogging();
        _uiBehavior.Changed += OnUiBehaviorChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;
        _updates.Changed += OnUpdatesChanged;
        _updateFlow.StateChanged += OnUpdateFlowStateChanged;
        _loggingSettings.Changed += OnLoggingSettingsChanged;
        _alias.Changed += OnClientAliasMapChanged;
        _traceCodeRule.Changed += OnTraceCodeRuleChanged;
        _barcodeGenSettings.Changed += OnBarcodeGenSettingsChanged;
        _apiAvailability.Changed += OnPacApiAvailabilityChanged;
        RefreshOpsUnlock();

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

    private void OnClientAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsClientAliasesEmpty));

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        SyncPageAvailability();
        RefreshOpsUnlock();
        _pageWorkCancelled = false;
        ReloadAgentsRuntime();
        RefreshUnsaved();
        SyncPacApiInfo();
        ReloadClientAliasesIfVisible("client_alias.reload.activate_fail");
        // 游标走 PacAPI；服务就绪才请求
        if (ConnectionView.IsReady(_apiAvailability.Current, _apiAvailability.IsConfigured))
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
                _logger.Warn("SettingsVM", eventName, "Settings background operation skipped toast (service unavailable)", ex);
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
        LoggingDirectory = LogDirectory.Resolve(options.LogDirectory).BrowseDirectory;
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

    protected override void DisposeCore()
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
        try { _barcodeGenSettings.Changed -= OnBarcodeGenSettingsChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.barcode_gen_unsub_fail", "Failed to unsubscribe BarcodeGen settings", ex);
        }
        try { _apiAvailability.Changed -= OnPacApiAvailabilityChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.pacapi_availability_unsub_fail", "Failed to unsubscribe PacApi availability", ex);
        }
        DisposeOpsUnlock();
        ClientAliases.CollectionChanged -= OnClientAliasesChanged;
        DisposeAgents();
        _pageWorkCts.Dispose();
        base.DisposeCore();
    }
}
