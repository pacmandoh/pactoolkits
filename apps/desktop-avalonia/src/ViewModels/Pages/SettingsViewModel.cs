using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public interface ISettingsPage { }

public partial class SettingsViewModel : AppPageBase, ISettingsPage
{
    public override string Icon => "Settings";
    public override int Index => 999;
    public override string DisplayName => "设置";
    public override bool ShowInSidebar => false;
    public override ICommand? RefreshCommand => null;

    private readonly ISettingsService _settings;
    private readonly IDbConfigService _svc;
    private readonly IToastService _toast;
    private readonly IClientAliasService _alias;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IUiBehaviorService _uiBehavior;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IAppUpdateService _updates;
    private readonly IReleaseChannelSwitchService _releaseChannelSwitch;
    private readonly IUpdateUiFlowService _updateUiFlow;
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

    [ObservableProperty] private string? _status;

    [ObservableProperty] private bool _isDbConnected;
    [ObservableProperty] private bool _isClientAliasRefreshing;
    [ObservableProperty] private bool _canSaveClientAliases;
    [ObservableProperty] private string _clientAliasHint = "加载中…";
    [ObservableProperty] private int _traceCodeRequiredLength = 20;
    [ObservableProperty] private string _traceCodePattern = "^8\\d+$";
    [ObservableProperty] private string _traceCodeRuleHint = "默认：长度 20，正则 ^8\\d+$";
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
    [ObservableProperty] private string _updateStatusHint = "未检查更新";
    [ObservableProperty] private string _updateChannelSwitchHint = "切换通道前会检查目标 Feed 与数据库兼容范围";
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _isUpdateApplying;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private bool _loggingEnabled = true;
    [ObservableProperty] private string _loggingMinimumLevel = "Error";
    [ObservableProperty] private int _loggingRetentionDays = 14;
    [ObservableProperty] private int _loggingMaxFileSizeMb = 20;
    [ObservableProperty] private string _loggingDirectory = string.Empty;
    [ObservableProperty] private string _loggingStatusHint = "日志系统已启用";
    [ObservableProperty] private bool _isLoggingBusy;
    [ObservableProperty] private string _dbSchemaCurrentVersion = "unknown";
    [ObservableProperty] private string _dbSchemaTargetVersion = "unknown";
    [ObservableProperty] private string _dbSchemaRequiredMinVersion = "unknown";
    [ObservableProperty] private string _dbSchemaRequiredMaxVersion = "unknown";
    [ObservableProperty] private string _dbSchemaStatusText = "未检查";
    [ObservableProperty] private bool _isDbSchemaSatisfied;
    [ObservableProperty] private bool _isDbSchemaChecking;
    [ObservableProperty] private bool _isDbSchemaFailed;
    [ObservableProperty] private string _dbSchemaErrorText = string.Empty;
    [ObservableProperty] private bool? _dbSchemaBadgeStatus;
    [ObservableProperty] private string _dbSchemaBadgeLabel = "未检查";
    [ObservableProperty] private string _dbSchemaLastCheckedAtText = "--";
    [ObservableProperty] private string _dbSchemaLastCheckSourceText = "--";
    [ObservableProperty] private string _dbSchemaLastMigrationText = "尚无迁移记录";
    [ObservableProperty] private string _dbSchemaMigrationPlanText = "尚未查看迁移计划";
    [ObservableProperty] private string _dbSchemaPolicyText = DatabaseMigrationPolicies.StableOnly;
    public string DbSchemaBetaConceptText
        => string.Equals(_releaseVersion.Current.BuildChannel, "beta", StringComparison.OrdinalIgnoreCase)
            ? "当前是 Beta 应用。Beta 应用与 Beta 数据库是两个独立概念；默认不会升级共享生产数据库。"
            : "应用发布通道与数据库环境相互独立；数据库迁移始终受清单策略和环境授权约束。";
    [ObservableProperty] private string _msfxGatewayUrl = "https://eco.taobao.com/router/rest";
    [ObservableProperty] private string _msfxAppKey = string.Empty;
    [ObservableProperty] private string _msfxAppSecret = string.Empty;
    [ObservableProperty] private bool _showMsfxAppSecret;
    [ObservableProperty] private string _msfxSessionToken = string.Empty;
    [ObservableProperty] private string _msfxRefEntId = string.Empty;
    [ObservableProperty] private int _msfxTimeoutSeconds = 20;
    [ObservableProperty] private string _msfxApiHint = "未保存";
    [ObservableProperty] private bool? _msfxApiBadgeStatus;
    [ObservableProperty] private string _msfxApiBadgeLabel = "未配置";
    public char MsfxAppSecretPasswordChar => ShowMsfxAppSecret ? '\0' : '•';

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
    public string LoggingMinimumLevelHint => LoggingMinimumLevel switch
    {
        "Debug" => "记录最详细调试信息，适合临时排障",
        "Info" => "记录关键流程信息，便于常规回溯",
        "Warn" => "仅记录异常征兆与潜在问题",
        "Error" => "仅记录错误与失败，推荐日常运行",
        "Fatal" => "仅记录致命故障，最小日志开销",
        _ => "日志级别未识别，将使用 Error"
    };
    public string DbSchemaStatusBadgeText => DbSchemaStatusText;
    public SettingsViewModel(
        IAppConfigStore appConfigStore,
        ISettingsService settings,
        IDbConfigService svc,
        IToastService toast,
        IClientAliasService alias,
        ITraceCodeRuleService traceCodeRule,
        IUiBehaviorService uiBehavior,
        IUpdateSettingsService updateSettings,
        IAppUpdateService updates,
        IReleaseChannelSwitchService releaseChannelSwitch,
        IUpdateUiFlowService updateUiFlow,
        IReleaseVersionService releaseVersion,
        IDialogService dialog,
        ILoggingSettingsService loggingSettings,
        IAppLogger logger,
        IClipboardService clipboard)
    {
        _appConfigStore = appConfigStore;
        _settings = settings;
        _svc = svc;
        _toast = toast;
        _alias = alias;
        _traceCodeRule = traceCodeRule;
        _uiBehavior = uiBehavior;
        _updateSettings = updateSettings;
        _updates = updates;
        _releaseChannelSwitch = releaseChannelSwitch;
        _updateUiFlow = updateUiFlow;
        _releaseVersion = releaseVersion;
        _dialog = dialog;
        _loggingSettings = loggingSettings;
        _logger = logger;
        _clipboard = clipboard;
        ClientAliases.CollectionChanged += OnClientAliasesChanged;
        var c = svc.Current;
        _host = c.Host;
        _port = c.Port;
        _database = c.Database;
        _username = c.Username;
        _password = c.Password;

        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        LoadAliasesOnly();
        UpdateClientAliasUiState();

        SafeFireAndForget(ReloadClientAliasesAsync, "client_alias.reload.startup_fail");

        LoadTraceCodeRule();
        LoadMsfxApiOptions();
        LoadUiBehavior();
        LoadUpdateOptions();
        LoadLoggingOptions();
        DbSchemaPolicyText = _releaseVersion.Current.DatabaseMigrationPolicy;
        SafeFireAndForget(RefreshDbSchemaStatusOnStartupAsync, "db.schema.startup_refresh.fire_and_forget_fail");
        _uiBehavior.Changed += OnUiBehaviorChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;
        _updates.Changed += OnUpdatesChanged;
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
        RefreshMsfxApiHint(options);
    }

    public Task RefreshDbSchemaStatusFromHostAsync(string source = "startup_postcheck")
        => RefreshDbSchemaStatusAsync(source, manualProbe: false);

    public void ResetDraftFromCurrent()
    {
        var cfg = _appConfigStore.Load();
        var c = cfg.Postgres ?? _svc.Current;
        Host = c.Host;
        Port = c.Port;
        Database = c.Database;
        Username = c.Username;
        Password = c.Password;

        Status = null;
        ShowMsfxAppSecret = false;
        IsClientAliasEditMode = false;
        IsClientAliasReadOnly = true;

        LoadAliasesOnly();
        UpdateClientAliasUiState();
        LoadTraceCodeRule();
        LoadMsfxApiOptions();
        LoadUiBehavior();
        LoadUpdateOptions();
        LoadLoggingOptions();

        // NumericUpDown can keep transient editor text (e.g. cleared but not committed).
        // Force notify all numeric fields so UI rebinds to persisted/current values.
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(TraceCodeRequiredLength));
        OnPropertyChanged(nameof(UpdatePollIntervalMinutes));
        OnPropertyChanged(nameof(LoggingRetentionDays));
        OnPropertyChanged(nameof(LoggingMaxFileSizeMb));
        OnPropertyChanged(nameof(MsfxTimeoutSeconds));

        // Force NumericUpDown editor text to rebind even when target value equals current value.
        var targetPort = c.Port;
        var targetTraceLength = TraceCodeRequiredLength;
        var targetPollMinutes = UpdatePollIntervalMinutes;
        var targetRetentionDays = LoggingRetentionDays;
        var targetFileSizeMb = LoggingMaxFileSizeMb;
        var targetMsfxTimeout = MsfxTimeoutSeconds;

        Port = targetPort == 1 ? 2 : 1;
        TraceCodeRequiredLength = targetTraceLength == 1 ? 2 : 1;
        UpdatePollIntervalMinutes = targetPollMinutes == 0 ? 1 : 0;
        LoggingRetentionDays = targetRetentionDays == 1 ? 2 : 1;
        LoggingMaxFileSizeMb = targetFileSizeMb == 1 ? 2 : 1;
        MsfxTimeoutSeconds = targetMsfxTimeout <= 3 ? 4 : 3;

        Port = targetPort;
        TraceCodeRequiredLength = targetTraceLength;
        UpdatePollIntervalMinutes = targetPollMinutes;
        LoggingRetentionDays = targetRetentionDays;
        LoggingMaxFileSizeMb = targetFileSizeMb;
        MsfxTimeoutSeconds = targetMsfxTimeout;
    }

    private void OnClientAliasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(IsClientAliasesEmpty));

    public override Task OnPageActivatedAsync(CancellationToken ct = default)
    {
        _pageWorkCancelled = false;
        return Task.CompletedTask;
    }

    public override Task OnPageDeactivatedAsync(CancellationToken ct = default)
    {
        CancelPageWork();
        return Task.CompletedTask;
    }

    private void SafeFireAndForget(Func<CancellationToken, Task> work, string eventName)
    {
        var token = _pageWorkCts.Token;
        _ = RunSafeFireAndForgetAsync(work, eventName, token);
    }

    private async Task RunSafeFireAndForgetAsync(Func<CancellationToken, Task> work, string eventName, CancellationToken ct)
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

    private Task SetBusyOnUiAsync(bool value)
        => RunOnUiAsync(() => IsBusy = value);

    private Task SetClientAliasRefreshingOnUiAsync(bool value)
        => RunOnUiAsync(() => IsClientAliasRefreshing = value);

    private Task ReportErrorOnUiAsync(string title, string message)
        => RunOnUiAsync(() => _toast.Error(title, message));

    private void PostUiSafe(Action action, string eventName)
    {
        _ = RunUiSafeAsync(action, eventName);
    }

    private async Task RunUiSafeAsync(Action action, string eventName)
    {
        try
        {
            await RunOnUiAsync(action);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", eventName, "Settings UI continuation failed", ex);
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
        RefreshUpdatePollIntervalHint();

        SyncUpdateStateFromService();

        _syncingUpdateOptions = false;
    }

    private void OnUiBehaviorChanged()
    {
        PostUiSafe(() =>
        {
            var ui = _uiBehavior.Current;
            _syncingUiBehavior = true;
            MinimizeToTrayOnClose = ui.MinimizeToTrayOnClose;
            _syncingUiBehavior = false;
        }, "ui_behavior.changed.ui_fail");
    }

    private void OnUpdateSettingsChanged()
    {
        PostUiSafe(LoadUpdateOptions, "update_settings.changed.ui_fail");
    }

    private void OnUpdatesChanged()
    {
        PostUiSafe(SyncUpdateStateFromService, "updates.changed.ui_fail");
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
        LoggingStatusHint = options.Enabled
            ? $"已启用（{options.MinimumLevel}）"
            : "已禁用";
        _syncingLoggingOptions = false;
    }

    private void OnLoggingSettingsChanged()
    {
        PostUiSafe(LoadLoggingOptions, "logging_settings.changed.ui_fail");
    }

    partial void OnProductUpdateAvailableChanged(bool? value)
        => OnPropertyChanged(nameof(ProductUpdateAvailabilityLabel));

    private void SyncUpdateStateFromService()
    {
        CurrentProductVersion = _updates.CurrentVersion;
        LatestProductVersion = _updates.LatestVersion;
        ProductUpdateAvailable = _updates.HasProductUpdateAvailable;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;
        UpdateStatusHint = _updates.LastMessage;
    }

    partial void OnLoggingMinimumLevelChanged(string value)
        => OnPropertyChanged(nameof(LoggingMinimumLevelHint));

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        if (_syncingUiBehavior)
            return;

        SafeFireAndForget(ct => SaveUiBehaviorImmediateAsync(value, ct), "ui_behavior.save.fire_and_forget_fail");
    }

    partial void OnUpdateChannelChanged(string value)
        => RefreshUpdatePollIntervalHint();

    partial void OnUpdatePollIntervalMinutesChanged(int value)
        => RefreshUpdatePollIntervalHint();

    private void RefreshUpdatePollIntervalHint()
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
        TraceCodeRuleHint = $"当前：长度 {rule.RequiredLength}，正则 {rule.Pattern}";
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

    [RelayCommand]
    private void StartEditClientAliases()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        IsClientAliasEditMode = true;
        IsClientAliasReadOnly = false;
        UpdateClientAliasUiState();
        SafeFireAndForget(ReloadClientAliasesAsync, "client_alias.reload.edit_start_fail");
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        IsBusy = true;
        Status = null;

        try
        {
            var opt = ToOptions();
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));

            var validation = await _settings.ValidateDatabaseConnectionAsync(
                opt,
                BuildSchemaContext(),
                cts.Token);

            if (!validation.ConnectionOk)
            {
                Status = validation.ConnectionSummary;
                IsDbConnected = false;
                UpdateClientAliasUiState();
                _toast.Error("数据库连接失败", validation.ConnectionSummary ?? "连接失败");
                return;
            }

            if (!validation.SchemaMigrationOk)
            {
                var reason = validation.MigrationSummary ?? "数据库结构更新失败";
                Status = reason;
                IsDbConnected = false;
                UpdateClientAliasUiState();
                _toast.Warn("数据库迁移策略", reason);
                return;
            }

            if (!validation.SchemaCompatible)
            {
                Status = "数据库版本不兼容";
                IsDbConnected = false;
                UpdateClientAliasUiState();
                await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), validation.IncompatibleMessage ?? "数据库版本不兼容");
                return;
            }

            Status = "连接成功";
            IsDbConnected = true;
            SafeFireAndForget(ReloadClientAliasesAsync, "client_alias.reload.after_test_fail");
            _toast.Success("数据库连接", "连接成功");
        }
        catch (OperationCanceledException)
        {
            if (IsPageWorkCancellation())
            {
                return;
            }

            _logger.Warn("SettingsVM", "db.test.timeout", "DB connection test timed out");
            Status = "连接超时";
            IsDbConnected = false;
            UpdateClientAliasUiState();
            _toast.Error("数据库连接失败", "连接超时：请检查网络/主机/端口");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _settings.SaveDatabaseConfigAsync(ToOptions(), _pageWorkCts.Token);
            if (!await EnsureDbSchemaUpToDateAsync())
            {
                Status = "配置已保存，但迁移失败，当前不可用";
                IsDbConnected = false;
                UpdateClientAliasUiState();
                _toast.Warn("数据库配置", "配置已保存，但迁移失败，当前不可用");
                return;
            }
            if (!await EnsureDbSchemaCompatibleAsync())
            {
                Status = "配置已保存，但数据库版本不兼容，当前不可用";
                IsDbConnected = false;
                UpdateClientAliasUiState();
                _toast.Warn("数据库配置", "配置已保存，但数据库版本不兼容，当前不可用");
                return;
            }

            Status = "连接成功";
            IsDbConnected = true;
            _toast.Success("配置已保存", "数据库配置已应用");

            SafeFireAndForget(ReloadClientAliasesAsync, "client_alias.reload.after_save_fail");
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && IsPageWorkCancellation())
            {
                return;
            }

            _logger.Error("SettingsVM", "db.save.fail", "Failed to save DB settings", ex);
            IsDbConnected = false;
            UpdateClientAliasUiState();
            _toast.Error("保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckDbSchemaStatusAsync()
    {
        if (ShouldSkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        await RefreshDbSchemaStatusAsync(
            "manual_check",
            manualProbe: true,
            connectionOptions: ToOptions(),
            operationCt: _pageWorkCts.Token);
    }

    [RelayCommand]
    private async Task ViewDbSchemaMigrationPlanAsync()
    {
        if (ShouldSkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        SetDbSchemaStatus("读取计划", checking: true, failed: false, error: null);
        try
        {
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(30));
            var plan = await _settings.GetSchemaMigrationPlanAsync(
                BuildSchemaContext(),
                ToOptions(),
                cts.Token);
            DbSchemaMigrationPlanText = FormatMigrationPlan(plan);
            _toast.Info("数据库迁移计划", DbSchemaMigrationPlanText);
            await RefreshDbSchemaStatusAsync(
                "migration_plan",
                manualProbe: false,
                connectionOptions: ToOptions(),
                operationCt: cts.Token);
        }
        catch (Exception ex)
        {
            DbSchemaMigrationPlanText = $"读取迁移计划失败：{ex.Message}";
            SetDbSchemaStatus("计划失败", checking: false, failed: true, error: ex.Message);
            _toast.Error("数据库迁移计划", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyDbSchemaUpdateAsync()
    {
        if (ShouldSkipTrigger() || IsDbSchemaChecking)
        {
            return;
        }

        var options = ToOptions();
        var snapshot = await _settings.ReadSchemaStatusAsync(
            BuildSchemaContext(),
            options,
            _pageWorkCts.Token);
        if (snapshot.ManualMigrationPolicy.Decision == DatabaseMigrationDecision.RequiresConfirmation)
        {
            var confirmed = await _dialog.Confirm(
                "确认更新数据库",
                $"{snapshot.ManualMigrationPolicy.Reason}\n\n此操作将修改 Beta 隔离测试库结构，是否继续？");
            if (!confirmed)
            {
                return;
            }

            await EnsureDbSchemaUpToDateAsync(options, userConfirmed: true);
            return;
        }

        await EnsureDbSchemaUpToDateAsync(options);
    }

    [RelayCommand]
    private async Task CopyDbSchemaDiagnosticsAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        var text = BuildDbSchemaDiagnosticsText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _toast.Warn("数据库结构更新", "当前无可复制的诊断信息");
            return;
        }

        await _clipboard.SetTextAsync(text);
        _toast.Success("数据库结构更新", "已复制诊断信息");
    }

    private PgOptions ToOptions() => new()
    {
        Host = Host,
        Port = Port,
        Database = Database,
        Username = Username,
        Password = Password
    };

    [RelayCommand]
    private async Task ReloadClientAliasesAsync(CancellationToken pageCt)
    {
        if (IsClientAliasRefreshing || ShouldSkipTrigger())
        {
            return;
        }

        pageCt.ThrowIfCancellationRequested();
        await SetClientAliasRefreshingOnUiAsync(true);
        try
        {
            var opt = ToOptions();
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(6));

            var loaded = await _settings.LoadClientAliasSourcesAsync(opt, cts.Token);
            var isDbConnected = loaded.IsDbConnected;
            var aliasMap = NormalizeAliasMapByMachine(_alias.GetAll());
            var clientMachines = new HashSet<string>(loaded.ClientMachines, StringComparer.OrdinalIgnoreCase);

            await RunOnUiAsync(() =>
            {
                IsDbConnected = isDbConnected;
                UntrackAllAliasRows();
                ClientAliases.Clear();

                foreach (var key in clientMachines.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    aliasMap.TryGetValue(key, out var a);
                    var row = new ClientAliasRow(key, a ?? string.Empty);
                    ClientAliases.Add(row);
                    TrackAliasRow(row);
                }

                foreach (var kv in aliasMap.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (clientMachines.Contains(kv.Key))
                    {
                        continue;
                    }

                    var row = new ClientAliasRow(kv.Key, kv.Value);
                    ClientAliases.Add(row);
                    TrackAliasRow(row);
                }

                UpdateClientAliasUiState();
            });
        }
        finally
        {
            await SetClientAliasRefreshingOnUiAsync(false);
        }
    }

    private void UpdateClientAliasUiState()
    {
        if (!IsClientAliasEditMode)
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "未找到任何机器标识，点击‘开始编辑’后可编辑别名"
                : "已加载机器标识/本地别名，点击‘开始编辑’后可编辑";

            CanSaveClientAliases = false;
            IsClientAliasReadOnly = true;
            return;
        }

        IsClientAliasReadOnly = false;

        if (!IsDbConnected)
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "未连接数据库：无法读取客户端列表，仍可编辑/保存本地别名；连接后可自动补全列表"
                : "未连接数据库：当前显示本地别名；连接后可自动补全客户端列表";
        }
        else
        {
            ClientAliasHint = ClientAliases.Count == 0
                ? "已连接，但暂无可用机器标识"
                : "在右侧填写别名，留空表示使用机器标识";
        }

        CanSaveClientAliases = true;
    }

    private void RemoveClientAlias(ClientAliasRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (IsClientAliasReadOnly)
        {
            return;
        }

        ClientAliases.Remove(row);
        UntrackAliasRow(row);
        UpdateClientAliasUiState();
    }

    [RelayCommand]
    private async Task SaveClientAliasesAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        if (!IsClientAliasEditMode)
        {
            _toast.Error("客户端别名", "请先点击‘开始编辑’");
            return;
        }

        UpdateClientAliasUiState();

        IsBusy = true;
        try
        {
            var items = ClientAliases
                .Where(x => !string.IsNullOrWhiteSpace(x.Alias))
                .Select(x => new KeyValuePair<string, string>(x.Machine, x.Alias));
            _alias.ReplaceAll(items);

            IsClientAliasEditMode = false;
            IsClientAliasReadOnly = true;

            Status = "客户端别名已保存";
            _toast.Success("客户端别名", "已保存并生效");
            await Task.Delay(600);
            Status = null;

            SafeFireAndForget(ReloadClientAliasesAsync, "client_alias.reload.after_alias_save_fail");
            UpdateClientAliasUiState();
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "client_alias.save.fail", "Failed to save client aliases", ex);
            _toast.Error("保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveTraceCodeRuleAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        if (TraceCodeRequiredLength <= 0)
        {
            _toast.Error("追溯码规则", "长度必须大于 0");
            return;
        }

        if (string.IsNullOrWhiteSpace(TraceCodePattern))
        {
            _toast.Error("追溯码规则", "正则表达式不能为空");
            return;
        }

        try
        {
            _ = Regex.IsMatch(string.Empty, TraceCodePattern);
        }
        catch (Exception ex)
        {
            _logger.Warn("SettingsVM", "trace_rule.regex_invalid", "Invalid trace regex pattern", ex, new { TraceCodePattern });
            _toast.Error("追溯码规则", $"正则格式错误：{ex.Message}");
            return;
        }

        IsBusy = true;
        try
        {
            await _traceCodeRule.SaveAsync(new TraceCodeValidationOptions
            {
                RequiredLength = TraceCodeRequiredLength,
                Pattern = TraceCodePattern
            });

            TraceCodeRuleHint = $"当前：长度 {TraceCodeRequiredLength}，正则 {TraceCodePattern}";
            _toast.Success("追溯码规则", "规则已保存并生效");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "trace_rule.save.fail", "Failed to save trace code rule", ex);
            _toast.Error("追溯码规则保存失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleMsfxAppSecretVisibility()
        => ShowMsfxAppSecret = !ShowMsfxAppSecret;

    partial void OnShowMsfxAppSecretChanged(bool value)
        => OnPropertyChanged(nameof(MsfxAppSecretPasswordChar));

    [RelayCommand]
    private async Task SaveMsfxApiConfigAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(MsfxAppKey))
        {
            _toast.Error("码上放心 API", "AppKey 不能为空");
            return;
        }

        if (string.IsNullOrWhiteSpace(MsfxAppSecret))
        {
            _toast.Error("码上放心 API", "AppSecret 不能为空");
            return;
        }

        IsBusy = true;
        try
        {
            var gateway = string.IsNullOrWhiteSpace(MsfxGatewayUrl)
                ? MsfxDefaultGatewayUrl
                : MsfxGatewayUrl.Trim();
            var appKey = MsfxAppKey.Trim();
            var appSecret = MsfxAppSecret.Trim();
            var sessionToken = (MsfxSessionToken ?? string.Empty).Trim();
            var refEntId = (MsfxRefEntId ?? string.Empty).Trim();
            var timeoutSeconds = Math.Clamp(MsfxTimeoutSeconds, 3, 120);

            await _appConfigStore.UpdateAsync(cfg =>
            {
                cfg.MsfxApi = new MsfxApiOptions
                {
                    GatewayUrl = gateway,
                    AppKey = appKey,
                    AppSecret = appSecret,
                    SessionToken = sessionToken,
                    RefEntId = refEntId,
                    DefaultMethod = "alibaba.alihealth.drugtrace.top.yljg.listupout",
                    TimeoutSeconds = timeoutSeconds
                };
            });

            var saved = _appConfigStore.Load().MsfxApi ?? new MsfxApiOptions();
            await RunOnUiAsync(() =>
            {
                MsfxGatewayUrl = string.Equals(saved.GatewayUrl, MsfxDefaultGatewayUrl, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : saved.GatewayUrl;
                MsfxAppKey = saved.AppKey;
                MsfxAppSecret = saved.AppSecret;
                MsfxSessionToken = saved.SessionToken;
                MsfxRefEntId = saved.RefEntId;
                MsfxTimeoutSeconds = saved.TimeoutSeconds;
                RefreshMsfxApiHint(saved);
            });

            _toast.Success("码上放心 API", "配置已保存");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "msfx.settings.save.fail", "Failed to save msfx api settings", ex);
            _toast.Error("码上放心 API", $"保存失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshMsfxApiHint(MsfxApiOptions options)
    {
        var hasKey = !string.IsNullOrWhiteSpace(options.AppKey);
        var hasSecret = !string.IsNullOrWhiteSpace(options.AppSecret);
        var hasEnt = !string.IsNullOrWhiteSpace(options.RefEntId);
        var hasToken = !string.IsNullOrWhiteSpace(options.SessionToken);
        var hasCore = hasKey || hasSecret || hasEnt;

        if (!hasCore)
        {
            MsfxApiHint = "配置状态：未配置";
            MsfxApiBadgeStatus = null;
            MsfxApiBadgeLabel = "未配置";
            return;
        }

        if (hasKey && hasSecret && hasEnt && hasToken)
        {
            MsfxApiHint = "配置状态：已就绪（含 SessionToken）";
            MsfxApiBadgeStatus = false;
            MsfxApiBadgeLabel = "已就绪";
            return;
        }

        if (hasKey && hasSecret && hasEnt)
        {
            MsfxApiHint = "配置状态：已就绪（SessionToken 可选）";
            MsfxApiBadgeStatus = false;
            MsfxApiBadgeLabel = "已就绪";
            return;
        }

        MsfxApiHint = "配置状态：待完善（需 AppKey/AppSecret/企业ID）";
        MsfxApiBadgeStatus = true;
        MsfxApiBadgeLabel = "待完善";
    }

    private MsfxApiOptions BuildMsfxOptionsFromUi()
    {
        var gateway = string.IsNullOrWhiteSpace(MsfxGatewayUrl)
            ? MsfxDefaultGatewayUrl
            : MsfxGatewayUrl.Trim();
        return new MsfxApiOptions
        {
            GatewayUrl = gateway,
            AppKey = (MsfxAppKey ?? string.Empty).Trim(),
            AppSecret = (MsfxAppSecret ?? string.Empty).Trim(),
            SessionToken = (MsfxSessionToken ?? string.Empty).Trim(),
            RefEntId = (MsfxRefEntId ?? string.Empty).Trim(),
            DefaultMethod = "alibaba.alihealth.drugtrace.top.yljg.listupout",
            TimeoutSeconds = Math.Clamp(MsfxTimeoutSeconds, 3, 120)
        };
    }


    private async Task SaveUiBehaviorImmediateAsync(bool value, CancellationToken ct)
    {
        try
        {
            await _uiBehavior.SaveAsync(new UiBehaviorOptions
            {
                MinimizeToTrayOnClose = value
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "ui_behavior.save.fail", "Failed to save UI behavior", ex);
            await RunOnUiAsync(() =>
            {
                _syncingUiBehavior = true;
                MinimizeToTrayOnClose = _uiBehavior.Current.MinimizeToTrayOnClose;
                _syncingUiBehavior = false;
                _toast.Error("界面行为保存失败", ex.Message);
            });
        }
    }

    [RelayCommand]
    private async Task SaveUpdateOptionsAsync()
    {
        if (_syncingUpdateOptions || ShouldSkipTrigger())
        {
            return;
        }

        IsBusy = true;
        IsUpdateChecking = true;
        try
        {
            var previous = _updateSettings.Current;
            var targetChannel = NormalizeUpdateChannel(UpdateChannel);
            var channelChanged = !string.Equals(previous.Channel, targetChannel, StringComparison.Ordinal);
            if (channelChanged)
            {
                var switched = await TrySwitchUpdateChannelAsync(previous, targetChannel);
                if (!switched)
                {
                    return;
                }
            }

            var options = new UpdateOptions
            {
                AutoCheckOnStartup = AutoCheckUpdateOnStartup,
                Channel = targetChannel,
                ValidatedChannel = channelChanged ? targetChannel : previous.ValidatedChannel,
                FeedUrl = UpdateFeedUrl,
                AutoCheckIntervalMinutes = Math.Clamp(UpdatePollIntervalMinutes, 0, 720),
                IgnoredVersion = IgnoredProductVersion
            };

            await _updateSettings.SaveAsync(options);
            _toast.Success("更新设置", "更新配置已保存");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.settings.save.fail", "Failed to save update settings", ex);
            _toast.Error("更新设置保存失败", ex.Message);
        }
        finally
        {
            SyncUpdateStateFromService();
            IsBusy = false;
        }
    }

    private async Task<bool> TrySwitchUpdateChannelAsync(UpdateOptions previous, string targetChannel)
    {
        if (targetChannel == "beta")
        {
            var confirmed = await _dialog.Confirm(
                "切换到 Beta 更新通道",
                "Beta 版本可能包含尚未完成验证的功能。切换前将检查 Beta Feed 和当前数据库兼容范围；不会执行数据库迁移。是否继续？");
            if (!confirmed)
            {
                RestoreUpdateChannel(previous.Channel);
                return false;
            }
        }

        using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(20));
        var probe = await _releaseChannelSwitch.ProbeAsync(
            UpdateFeedUrl,
            targetChannel,
            ToOptions(),
            cts.Token);
        UpdateChannelSwitchHint = probe.Message;
        if (!probe.Success)
        {
            RestoreUpdateChannel(previous.Channel);
            await _dialog.Warn("无法切换更新通道", probe.Message);
            return false;
        }

        _logger.Info("SettingsVM", "update.channel.switch.validated",
            "Release channel switch validated without database migration", new
            {
                PreviousChannel = previous.Channel,
                TargetChannel = targetChannel,
                probe.FeedManifestUrl,
                probe.CurrentDbSchema,
                probe.RequiredMinDbSchema,
                probe.RequiredMaxDbSchema
            });
        return true;
    }

    private void RestoreUpdateChannel(string channel)
    {
        _syncingUpdateOptions = true;
        UpdateChannel = channel;
        _syncingUpdateOptions = false;
        RefreshUpdatePollIntervalHint();
    }

    private static string NormalizeUpdateChannel(string? channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : "stable";
    }

    [RelayCommand]
    private async Task SaveLoggingOptionsAsync()
    {
        if (_syncingLoggingOptions || IsLoggingBusy || ShouldSkipTrigger())
        {
            return;
        }

        IsLoggingBusy = true;
        try
        {
            var options = new LoggingOptions
            {
                Enabled = LoggingEnabled,
                MinimumLevel = LoggingMinimumLevel,
                RetentionDays = LoggingRetentionDays,
                MaxFileSizeMb = LoggingMaxFileSizeMb,
                LogDirectory = LoggingDirectory
            };

            await _loggingSettings.SaveAsync(options);
            LoggingDirectory = _logger.LogDirectory;
            LoggingStatusHint = LoggingEnabled ? $"已启用（{LoggingMinimumLevel}）" : "已禁用";
            _toast.Success("日志设置", "日志配置已保存");
            _logger.Info("SettingsVM", "logging.settings.saved", "Logging settings updated", new
            {
                options.Enabled,
                options.MinimumLevel,
                options.RetentionDays,
                options.MaxFileSizeMb,
                LogDirectory = _logger.LogDirectory
            });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.settings.save_fail", "Failed to save logging settings", ex);
            _toast.Error("日志设置保存失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenLogDirectoryAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
        {
            return Task.CompletedTask;
        }

        IsLoggingBusy = true;
        try
        {
            var dir = _logger.LogDirectory;
            Directory.CreateDirectory(dir);
            OpenDirectory(dir);
            LoggingStatusHint = $"已打开：{dir}";
            _logger.Info("SettingsVM", "logging.open_dir", "Opened log directory", new { dir });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.open_dir_fail", "Failed to open log directory", ex);
            _toast.Error("打开日志目录失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }

        return Task.CompletedTask;
    }

    private static void OpenDirectory(string dir)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { dir },
                UseShellExecute = false
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { dir },
                UseShellExecute = false
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { dir },
            UseShellExecute = false
        });
    }

    [RelayCommand]
    private async Task CopyCurrentLogPathAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
        {
            return;
        }

        IsLoggingBusy = true;
        try
        {
            var path = _logger.CurrentLogPath;
            await _clipboard.SetTextAsync(path);
            LoggingStatusHint = $"已复制：{path}";
            _toast.Success("日志", "当前日志路径已复制到剪贴板");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.copy_path_fail", "Failed to copy log path", ex);
            _toast.Error("复制日志路径失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportRecentLogsAsync()
    {
        if (IsLoggingBusy || ShouldSkipTrigger())
        {
            return;
        }

        IsLoggingBusy = true;
        try
        {
            var path = await _logger.ExportRecentAsync(TimeSpan.FromHours(24));
            await _clipboard.SetTextAsync(path);
            LoggingStatusHint = $"已导出：{path}";
            _toast.Success("日志导出", "最近24小时日志已导出并复制路径");
            _logger.Info("SettingsVM", "logging.export_recent", "Exported recent logs", new { path, window = "24h" });
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "logging.export_recent_fail", "Failed to export recent logs", ex);
            _toast.Error("日志导出失败", ex.Message);
        }
        finally
        {
            IsLoggingBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_updates.IsChecking || IsUpdateApplying || ShouldSkipTrigger())
        {
            return;
        }

        await _updateUiFlow.CheckAndHandleAsync(
            showNoUpdateToast: true,
            startupMode: false,
            applyNowAction: ApplyUpdateNowAsync,
            ignoreVersionAction: IgnoreCurrentUpdateAsync,
            logScope: "SettingsVM");
    }

    [RelayCommand]
    private async Task ApplyUpdateNowAsync()
    {
        if (IsUpdateChecking || IsUpdateApplying || ShouldSkipTrigger())
        {
            return;
        }

        IsUpdateApplying = true;
        try
        {
            await _updateUiFlow.ApplyUpdateFlowAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.apply.fail", "Failed to apply update", ex);
            _toast.Error("应用更新", ex.Message);
        }
        finally
        {
            IsUpdateApplying = false;
        }
    }

    [RelayCommand]
    private async Task ClearIgnoredVersionAsync()
    {
        if (ShouldSkipTrigger())
        {
            return;
        }

        try
        {
            IgnoredProductVersion = string.Empty;
            await _updateSettings.SaveIgnoredVersionAsync(string.Empty);
            _toast.Success("更新设置", "已清除忽略版本");
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.clear_ignored.fail", "Failed to clear ignored version", ex);
            _toast.Error("更新设置", ex.Message);
        }
    }

    private async Task IgnoreCurrentUpdateAsync()
    {
        try
        {
            await _updateUiFlow.IgnoreVersionAsync(LatestProductVersion);
            IgnoredProductVersion = LatestProductVersion;
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "update.ignore.fail", "Failed to ignore update version", ex);
            _toast.Error("应用更新", ex.Message);
        }
    }

    private DbSchemaVersionContext BuildSchemaContext()
    {
        var version = _releaseVersion.Current;
        return new DbSchemaVersionContext(
            version.UiMinDbSchema,
            version.UiMaxDbSchema,
            version.AgentMinDbSchema,
            version.AgentMaxDbSchema,
            version.DbSchemaVersion,
            version.BuildChannel,
            version.DatabaseMigrationPolicy);
    }

    private async Task<bool> EnsureDbSchemaCompatibleAsync(PgOptions? connectionOptions = null)
    {
        var options = connectionOptions ?? ToOptions();
        var compat = await _settings.CheckSchemaCompatibilityAsync(
            BuildSchemaContext(),
            options,
            _pageWorkCts.Token);
        await RefreshDbSchemaStatusAsync(
            "compat_check",
            manualProbe: false,
            connectionOptions: options,
            operationCt: _pageWorkCts.Token);
        if (compat.Compatible)
        {
            return true;
        }

        await _dialog.Warn(DbSchemaCompat.GetIncompatibleTitle(), compat.IncompatibleMessage ?? "数据库版本不兼容");
        return false;
    }

    private Task<bool> EnsureDbSchemaUpToDateAsync(bool userConfirmed = false)
        => EnsureDbSchemaUpToDateAsync(ToOptions(), userConfirmed);

    private async Task<bool> EnsureDbSchemaUpToDateAsync(
        PgOptions connectionOptions,
        bool userConfirmed = false)
    {
        SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);
        try
        {
            using var cts = CreatePageOperationCts(TimeSpan.FromSeconds(120));
            var status = await _settings.ReadSchemaStatusAsync(
                BuildSchemaContext(),
                connectionOptions,
                cts.Token);
            if (status.Compatibility == DbSchemaCompatibility.AboveMaximum)
            {
                DbSchemaCurrentVersion = status.CurrentVersion ?? "unknown";
                DbSchemaTargetVersion = status.TargetVersion;
                DbSchemaRequiredMinVersion = status.RequiredMinVersion;
                DbSchemaRequiredMaxVersion = status.RequiredMaxVersion;
                var message =
                    $"数据库版本高于当前程序支持范围：当前 {status.CurrentVersion}，最高支持 {status.RequiredMaxVersion}。不会执行自动降级。";
                SetDbSchemaStatus("版本过高", checking: false, failed: true, error: message);
                _toast.Error("数据库结构更新", message);
                return false;
            }

            var migration = await _settings.EnsureSchemaUpToDateAsync(
                BuildSchemaContext(),
                DatabaseMigrationTrigger.SettingsManual,
                connectionOptions,
                userConfirmed: userConfirmed,
                ciMigrationAuthorized: false,
                ct: cts.Token);
            if (!migration.Ok)
            {
                SetDbSchemaStatus("更新失败", checking: false, failed: true, error: migration.Summary);
                DbSchemaLastMigrationText = migration.Summary;
                _toast.Error("数据库结构更新", migration.Summary);
                return false;
            }

            DbSchemaLastMigrationText = migration.Summary;
            if (migration.Summary.Contains("applied=", StringComparison.Ordinal))
            {
                _toast.Success("数据库结构更新", "数据库结构已更新");
            }
            else if (status.ManualMigrationPolicy.Decision == DatabaseMigrationDecision.ReadOnlyRequired)
            {
                _toast.Warn("数据库结构更新", migration.Summary);
            }

            await RefreshDbSchemaStatusAsync(
                "migrate_done",
                manualProbe: false,
                connectionOptions: connectionOptions,
                operationCt: cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            SetDbSchemaStatus("更新失败", checking: false, failed: true, error: ex.Message);
            DbSchemaLastMigrationText = $"迁移失败：{ex.Message}";
            OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
            _toast.Error("数据库结构更新", $"更新失败：{ex.Message}");
            return false;
        }
    }

    private async Task RefreshDbSchemaStatusAsync(
        string source,
        bool manualProbe,
        PgOptions? connectionOptions = null,
        CancellationToken operationCt = default)
    {
        if (IsDbSchemaChecking && manualProbe)
        {
            return;
        }

        if (manualProbe)
        {
            SetDbSchemaStatus("更新中", checking: true, failed: false, error: null);
        }

        try
        {
            var ct = operationCt.CanBeCanceled ? operationCt : _pageWorkCts.Token;
            var options = connectionOptions ?? ToOptions();
            var snapshot = await _settings.ReadSchemaStatusAsync(BuildSchemaContext(), options, ct);
            DbSchemaLastCheckedAtText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DbSchemaLastCheckSourceText = MapDbSchemaCheckSource(source);
            DbSchemaTargetVersion = snapshot.TargetVersion;
            DbSchemaRequiredMinVersion = snapshot.RequiredMinVersion;
            DbSchemaRequiredMaxVersion = snapshot.RequiredMaxVersion;
            DbSchemaCurrentVersion = snapshot.CurrentVersion ?? "unknown";

            IsDbSchemaSatisfied = snapshot.Satisfied;
            UpdateManualMigrationPolicyState(snapshot.ManualMigrationPolicy);

            if (snapshot.Compatibility == DbSchemaCompatibility.MetadataMissing)
            {
                SetDbSchemaStatus(
                    "需要初始化",
                    checking: false,
                    failed: false,
                    error: snapshot.Reason ?? "数据库缺少迁移元数据，需要初始化");
                if (manualProbe)
                {
                    _toast.Warn("数据库结构更新", snapshot.Reason ?? "数据库缺少迁移元数据，需要初始化");
                }

                return;
            }

            if (!snapshot.SchemaOk)
            {
                SetDbSchemaStatus("未知", checking: false, failed: false, error: snapshot.Reason ?? "读取失败");
                if (manualProbe)
                {
                    _toast.Warn("数据库结构更新", $"状态未知：{snapshot.Reason ?? "读取失败"}");
                }

                return;
            }

            if (snapshot.Compatibility == DbSchemaCompatibility.AboveMaximum)
            {
                SetDbSchemaStatus(
                    "版本过高",
                    checking: false,
                    failed: true,
                    error: $"数据库版本高于当前程序支持范围：当前 {snapshot.CurrentVersion}，最高支持 {snapshot.RequiredMaxVersion}");
            }
            else if (!snapshot.Satisfied)
            {
                SetDbSchemaStatus("需要更新", checking: false, failed: false, error: $"当前版本 {snapshot.CurrentVersion} 低于最低要求 {snapshot.RequiredMinVersion}");
            }
            else if (snapshot.Updatable)
            {
                SetDbSchemaStatus("可更新", checking: false, failed: false, error: $"当前版本 {snapshot.CurrentVersion} 低于本地版本文件 {snapshot.TargetVersion}");
            }
            else
            {
                SetDbSchemaStatus("已满足", checking: false, failed: false, error: null);
            }

            if (manualProbe)
            {
                if (snapshot.Compatibility == DbSchemaCompatibility.AboveMaximum)
                {
                    _toast.Error("数据库结构更新", $"数据库版本高于当前程序支持范围，最高支持 {snapshot.RequiredMaxVersion}");
                }
                else if (!snapshot.Satisfied)
                {
                    _toast.Warn("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，低于最低要求 {snapshot.RequiredMinVersion}");
                }
                else if (snapshot.Updatable)
                {
                    _toast.Warn("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，可更新到本地版本 {snapshot.TargetVersion}");
                }
                else
                {
                    _toast.Success("数据库结构更新", $"当前版本 {snapshot.CurrentVersion}，满足最低要求 {snapshot.RequiredMinVersion}");
                }
            }
        }
        finally
        {
            IsDbSchemaChecking = false;
        }
    }

    private async Task RefreshDbSchemaStatusOnStartupAsync(CancellationToken ct)
    {
        try
        {
            await RefreshDbSchemaStatusAsync("startup", manualProbe: false, operationCt: ct);
        }
        catch (Exception ex)
        {
            _logger.Error("SettingsVM", "db.schema.startup_refresh.fail", "Startup schema status refresh failed", ex);
            SetDbSchemaStatus("未知", checking: false, failed: false, error: ex.Message);
            DbSchemaLastCheckedAtText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
            DbSchemaLastCheckSourceText = MapDbSchemaCheckSource("startup");
        }
    }

    private static string MapDbSchemaCheckSource(string source)
        => source switch
        {
            "startup" => "应用启动",
            "startup_postcheck" => "启动检查完成后回读",
            "open_settings" => "打开设置页",
            "db_reconnected" => "数据库重连后回读",
            "manual_check" => "手动检查",
            "migration_plan" => "查看迁移计划",
            "compat_check" => "兼容性校验",
            "migrate_done" => "迁移完成后回读",
            _ => source
        };

    private static string FormatMigrationPlan(DbSchemaMigrationPlan plan)
    {
        var pending = plan.Items.Where(x => !x.Applied).ToArray();
        if (pending.Length == 0)
        {
            return $"当前 {plan.CurrentVersion ?? "未初始化"}，目标 {plan.TargetVersion}，无待执行迁移";
        }

        var files = string.Join(", ", pending.Select(x => x.FileName));
        var bootstrap = plan.BootstrapRequired ? "，需要初始化迁移元数据" : string.Empty;
        return $"当前 {plan.CurrentVersion ?? "未初始化"}，目标 {plan.TargetVersion}，待执行 {pending.Length} 项{bootstrap}：{files}";
    }

    private string GetRequiredMinSchemaVersion()
    {
        var version = _releaseVersion.Current;
        var uiMin = DbSchemaCompat.NormalizeBound(version.UiMinDbSchema, version.DbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(version.AgentMinDbSchema, version.DbSchemaVersion);
        return DbSchemaCompat.GetRequiredMin(uiMin, agentMin);
    }

    private void SetDbSchemaStatus(string status, bool checking, bool failed, string? error)
    {
        DbSchemaStatusText = status;
        IsDbSchemaChecking = checking;
        IsDbSchemaFailed = failed;
        if (failed || status == "需要更新")
        {
            IsDbSchemaSatisfied = false;
        }

        DbSchemaErrorText = error ?? string.Empty;
        MapDbSchemaBadge(status, checking, failed);
        OnPropertyChanged(nameof(DbSchemaStatusBadgeText));
        OnPropertyChanged(nameof(CanCopyDbSchemaDiagnostics));
        OnPropertyChanged(nameof(CanApplyDbSchemaUpdate));
    }

    private void UpdateManualMigrationPolicyState(DatabaseMigrationPolicyResult policy)
    {
        CanApplyDbSchemaUpdateByPolicy =
            policy.Decision is DatabaseMigrationDecision.Allowed
                or DatabaseMigrationDecision.RequiresConfirmation;
        DbSchemaPolicyText = $"{_releaseVersion.Current.DatabaseMigrationPolicy}: {policy.Reason}";
        OnPropertyChanged(nameof(CanApplyDbSchemaUpdate));
    }

    private string BuildDbSchemaDiagnosticsText()
    {
        var lines = new List<string>
        {
            $"status={DbSchemaStatusText}",
            $"target={DbSchemaTargetVersion}",
            $"current={DbSchemaCurrentVersion}",
            $"required_min={DbSchemaRequiredMinVersion}",
            $"required_max={DbSchemaRequiredMaxVersion}",
            $"last_checked_at={DbSchemaLastCheckedAtText}",
            $"last_check_source={DbSchemaLastCheckSourceText}",
            $"last_migration={DbSchemaLastMigrationText}",
            $"migration_plan={DbSchemaMigrationPlanText}",
            $"policy={DbSchemaPolicyText}"
        };

        if (!string.IsNullOrWhiteSpace(DbSchemaErrorText))
        {
            lines.Add($"error={DbSchemaErrorText}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void MapDbSchemaBadge(string status, bool checking, bool failed)
    {
        if (checking || string.Equals(status, "更新中", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "更新中";
            return;
        }

        if (string.Equals(status, "版本过高", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "版本过高";
            return;
        }

        if (failed || string.Equals(status, "更新失败", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "更新失败";
            return;
        }

        if (string.Equals(status, "已满足", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = false;
            DbSchemaBadgeLabel = "已满足";
            return;
        }

        if (string.Equals(status, "可更新", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = true;
            DbSchemaBadgeLabel = "可更新";
            return;
        }

        if (string.Equals(status, "需要更新", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = true;
            DbSchemaBadgeLabel = "需要更新";
            return;
        }

        if (string.Equals(status, "未知", StringComparison.Ordinal)
            || string.Equals(status, "未检查", StringComparison.Ordinal))
        {
            DbSchemaBadgeStatus = null;
            DbSchemaBadgeLabel = "未知";
            return;
        }

        DbSchemaBadgeStatus = null;
        DbSchemaBadgeLabel = string.IsNullOrWhiteSpace(status) ? "未知" : status;
    }

    private static bool IsSchemaUpdatable(string? currentVersion, string? localTargetVersion)
    {
        if (!DbSchemaCompat.TryParseSemVer(currentVersion ?? string.Empty, out var current))
        {
            return false;
        }

        if (!DbSchemaCompat.TryParseSemVer(localTargetVersion ?? string.Empty, out var target))
        {
            return false;
        }

        return DbSchemaCompat.CompareSemVer(current, target) < 0;
    }

    private void TrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Add(row))
        {
            row.PropertyChanged += OnClientAliasRowPropertyChanged;
        }
    }

    private void UntrackAliasRow(ClientAliasRow row)
    {
        if (_trackedAliasRows.Remove(row))
        {
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
        }
    }

    private void UntrackAllAliasRows()
    {
        foreach (var row in _trackedAliasRows.ToArray())
        {
            row.PropertyChanged -= OnClientAliasRowPropertyChanged;
        }

        _trackedAliasRows.Clear();
    }

    private void OnClientAliasRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClientAliasRow.Alias))
        {
            UpdateClientAliasUiState();
        }
    }

    private static Dictionary<string, string> NormalizeAliasMapByMachine(IReadOnlyDictionary<string, string> source)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            var machine = ExtractMachine(kv.Key);
            if (machine.Length == 0)
            {
                continue;
            }

            var alias = (kv.Value ?? string.Empty).Trim();
            if (alias.Length == 0)
            {
                continue;
            }

            map[machine] = alias;
        }

        return map;
    }

    private static string ExtractMachine(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var parsed = ClientParser.Parse(text);
        return (parsed.Machine ?? text).Trim();
    }

    private static string GetAvailabilityLabel(bool? value) => value switch
    {
        true => "可更新",
        false => "已最新",
        _ => "未知"
    };

    public override void Dispose()
    {
        _disposed = true;
        _pageWorkCancelled = true;
        CancelPageWork();
        try { _uiBehavior.Changed -= OnUiBehaviorChanged; }
        catch (System.Exception ex)
        {
            _logger.Warn("SettingsVM", "dispose.ui_behavior_unsub_fail", "Failed to unsubscribe UiBehavior", ex);
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
