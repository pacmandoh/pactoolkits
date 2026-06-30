using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public interface ISettingsPage
{
    bool HasUnsavedChanges { get; }
    Task<bool> TrySaveOrDiscardAllAsync();
}

public partial class SettingsViewModel : AppPageBase, ISettingsPage
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
    private readonly IReleaseChannelService _releaseChannelService;
    private readonly IUpdateFlowService _updateFlow;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IDialogService _dialog;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IAppLogger _logger;
    private readonly IClipboardService _clipboard;
    private readonly HashSet<ClientAliasRow> _trackedAliasRows = new();
    private CancellationTokenSource _pageWorkCts = new();
    private bool _disposed;
    private bool _pageWorkCancelled;
    private bool _syncingUiBehavior;
    private bool _syncingUpdateOptions;
    private bool _syncingLoggingOptions;
    private const string MsfxDefaultGatewayUrl = "https://eco.taobao.com/router/rest";

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
    [ObservableProperty] private bool? _productUpdateAvailable;
    [ObservableProperty] private string _latestProductVersion = "unknown";
    [ObservableProperty] private string _updateChannelSwitchHint = "切换通道前会检查目标 Feed 与数据库兼容范围";
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _hasUpdateAvailable;
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
    [ObservableProperty] private string _dbSchemaLastMigrationText = "尚无迁移记录";
    [ObservableProperty] private string _dbSchemaMigrationPlanText = "尚未查看迁移计划";
    [ObservableProperty] private string _dbSchemaPolicyText = DbMigrationPolicies.StableOnly;
    public string DbSchemaBetaConceptText
        => string.Equals(_releaseVersion.Current.BuildChannel, "beta", StringComparison.OrdinalIgnoreCase)
            ? "当前是 Beta 应用，Beta 应用与 Beta 数据库是两个独立概念；默认不会升级共享生产数据库"
            : "应用发布通道与数据库环境相互独立；数据库迁移始终受清单策略和环境授权约束";
    [ObservableProperty] private string _msfxGatewayUrl = "https://eco.taobao.com/router/rest";
    [ObservableProperty] private string _msfxAppKey = string.Empty;
    [ObservableProperty] private string _msfxAppSecret = string.Empty;
    [ObservableProperty] private string _msfxSessionToken = string.Empty;
    [ObservableProperty] private string _msfxRefEntId = string.Empty;
    [ObservableProperty] private int _msfxTimeoutSeconds = 20;
    [ObservableProperty] private bool? _msfxApiBadgeStatus;
    [ObservableProperty] private string _msfxApiBadgeLabel = "未配置";

    [ObservableProperty] private bool _isClientAliasEditMode;
    [ObservableProperty] private bool _isClientAliasReadOnly = true;
    public bool CanCopyDbSchemaDiagnostics => !string.IsNullOrWhiteSpace(BuildDbSchemaDiagnosticsText());
    public bool CanApplyDbSchemaUpdate => !IsDbSchemaChecking
        && !string.Equals(DbSchemaStatusText, "更新中", StringComparison.Ordinal)
        && !string.Equals(DbSchemaStatusText, "版本过高", StringComparison.Ordinal)
        && CanApplyDbSchemaUpdateByPolicy;
    [ObservableProperty] private bool _canApplyDbSchemaUpdateByPolicy;
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
    public string ProductUpdateAvailabilityLabel => GetAvailabilityLabel(ProductUpdateAvailable);
    public bool IsUpdateApplying => _updateFlow.IsApplying;
    public bool CanApplyProductUpdateNow => HasUpdateAvailable && !IsUpdateChecking && !IsUpdateApplying;
    public string LoggingMinimumLevelHint => LoggingMinimumLevel switch
    {
        "Debug" => "记录最详细调试信息，适合临时排障",
        "Info" => "记录关键流程信息，便于常规回溯",
        "Warn" => "仅记录异常征兆与潜在问题",
        "Error" => "仅记录错误与失败，推荐日常运行",
        "Fatal" => "仅记录致命故障，最小日志开销",
        _ => "日志级别未识别，将使用 Error"
    };
    public SettingsViewModel(
        IAppConfigStore appConfigStore,
        ISettingsService settings,
        IToastService toast,
        IClientAliasService alias,
        ITraceCodeRuleService traceCodeRule,
        IUiBehaviorService uiBehavior,
        IUpdateSettingsService updateSettings,
        IAppUpdateService updates,
        IReleaseChannelService releaseChannelService,
        IUpdateFlowService updateFlow,
        IReleaseVersionService releaseVersion,
        IDialogService dialog,
        ILoggingSettingsService loggingSettings,
        IAppLogger logger,
        IClipboardService clipboard)
    {
        _appConfigStore = appConfigStore;
        _settings = settings;
        _toast = toast;
        _alias = alias;
        _traceCodeRule = traceCodeRule;
        _uiBehavior = uiBehavior;
        _updateSettings = updateSettings;
        _updates = updates;
        _releaseChannelService = releaseChannelService;
        _updateFlow = updateFlow;
        _releaseVersion = releaseVersion;
        _dialog = dialog;
        _loggingSettings = loggingSettings;
        _logger = logger;
        _clipboard = clipboard;
        ClientAliases.CollectionChanged += OnClientAliasesChanged;
        var c = settings.AppliedDb;
        _host = c.Host;
        _port = c.Port;
        _database = c.Database;
        _username = c.Username;
        _password = c.Password;

        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        LoadAliasesOnly();
        RefreshClientAlias();

        RunDetached(ReloadClientAliasesAsync, "client_alias.reload.startup_fail");

        LoadTraceCodeRule();
        LoadMsfxApiOptions();
        LoadUiBehavior();
        LoadUpdateOptions();
        LoadLoggingOptions();
        DbSchemaPolicyText = _releaseVersion.Current.DbMigrationPolicy;
        RunDetached(RefreshSchemaStatusOnStartupAsync, "db.schema.startup_refresh.fire_and_forget_fail");
        _uiBehavior.Changed += OnUiBehaviorChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;
        _updates.Changed += OnUpdatesChanged;
        _updateFlow.StateChanged += OnUpdateFlowStateChanged;
        _loggingSettings.Changed += OnLoggingSettingsChanged;

    }

    private void LoadMsfxApiOptions()
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
        => UpdateSchemaStatusAsync(source, manualProbe: false);

    private void OnClientAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsClientAliasesEmpty));

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        SyncPageAvailability();
        _pageWorkCancelled = false;
        RefreshUnsaved();
        return Task.CompletedTask;
    }

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelPageWork();
        _loggingAutoSaveCts?.Cancel();
        _updateAutoSaveCts?.Cancel();
        return Task.CompletedTask;
    }

    private void RunDetached(Func<CancellationToken, Task> work, string eventName)
    {
        var token = _pageWorkCts.Token;
        _ = RunDetachedAsync(work, eventName, token);
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
        _ = PostUiAsync(action, eventName);
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

    private void LoadUiBehavior()
    {
        var ui = _uiBehavior.Current;
        MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
    }

    private void LoadUpdateOptions()
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
        PostUi(LoadUpdateOptions, "update_settings.changed.ui_fail");
    }

    private void OnUpdatesChanged()
    {
        PostUi(SyncUpdateState, "updates.changed.ui_fail");
    }

    private void LoadLoggingOptions()
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
        PostUi(LoadLoggingOptions, "logging_settings.changed.fail");
    }

    partial void OnProductUpdateAvailableChanged(bool? value)
    {
        OnPropertyChanged(nameof(ProductUpdateAvailabilityLabel));
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
        ProductUpdateAvailable = _updates.HasProductUpdateAvailable;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;
    }

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

    private void LoadTraceCodeRule()
    {
        var rule = _traceCodeRule.Current;
        TraceCodeRequiredLength = rule.RequiredLength;
        TraceCodePattern = rule.Pattern;
    }

    private void LoadAliasesOnly()
    {
        UntrackAllAliasRows();
        ClientAliases.Clear();
        foreach (var kv in NormalizeAliasMapByMachine(_alias.GetAll()).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var row = new ClientAliasRow(kv.Key, kv.Value);
            ClientAliases.Add(row);
            TrackAliasRow(row);
        }
    }

    public override void Dispose()
    {
        _disposed = true;
        _pageWorkCancelled = true;
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
        ClientAliases.CollectionChanged -= OnClientAliasesChanged;
        _pageWorkCts.Dispose();
        base.Dispose();
    }
}
