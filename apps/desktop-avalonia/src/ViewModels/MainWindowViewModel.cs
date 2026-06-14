using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Collections;
using global::Avalonia.Styling;
using global::Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Application.Abstractions;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Models;
using SukiUI.Toasts;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public ISukiToastManager ToastManager { get; }
    public ISukiDialogManager DialogManager { get; }

    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionMonitorService _dbMonitor;
    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IDbSchemaMigrationService _dbSchemaMigration;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly IAgentManager _agentManager;
    private IAgentRuntime Injector => _agentManager.GetRequired(AgentIds.InjectorAhk);
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAppStartupStateService _startupState;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IUpdateUiFlowService _updateUiFlow;
    private readonly IAppLogger _logger;
    private readonly PageNavigationService _nav;
    private readonly string _configPath;
    private readonly string _configDir;
    private readonly string _configFile;
    private FileSystemWatcher? _configWatcher;
    private volatile bool _isApplyingConfig;
    private DateTimeOffset _lastDbErrorToastAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDbOkToastAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAhkTopToastAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan AhkTopToastDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan TopActionDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StartupDbMigrationTimeout = TimeSpan.FromSeconds(120);
    private string? _lastDbFailReason;
    private string? _lastSeenConfigJson;
    private bool _dbEverDisconnected;
    private int _dbReconnectMigrationRunning;

    private readonly TimeSpan _autoRefreshDebounce = TimeSpan.FromMilliseconds(180);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _dbBootstrapCts;
    private CancellationTokenSource? _updatePollCts;
    private CancellationTokenSource? _pageLifecycleCts;
    private readonly object _dirtyPagesGate = new();
    private readonly HashSet<AppPageBase> _dirtyPages = new();

    private readonly SukiTheme _theme = SukiTheme.GetInstance();
    private IAvaloniaReadOnlyList<SukiColorTheme> Themes => _theme.ColorThemes;

    private IReadOnlyList<AppPageBase> Pages { get; }
    public IReadOnlyList<AppPageBase> SidebarPages { get; }
    private readonly Dictionary<Type, AppPageBase> _pageByType;
    private readonly AppPageBase? _settingsPage;
    private readonly AppPageBase? _aboutPage;
    private AppPageBase? _activeLifecyclePage;
    private bool _disposed;

    private System.Windows.Input.ICommand? _lastRefreshCommand;
    private System.Windows.Input.ICommand? _lastImportCommand;
    private System.Windows.Input.ICommand? _lastExportCommand;


    [RelayCommand]
    private void OpenSettings()
    {
        if (ShouldSkipTrigger("main.nav.settings", 250))
            return;

        var page = _settingsPage;

        if (page is not null)
            ActivePage = page;
    }

    [RelayCommand]
    private void OpenAbout()
    {
        if (ShouldSkipTrigger("main.nav.about", 250))
            return;

        var page = _aboutPage;

        if (page is not null)
            ActivePage = page;
    }

    [ObservableProperty] private AppPageBase? _activePage;
    [ObservableProperty] private bool _isNightMode;
    [ObservableProperty] private bool _isDbProbeRunning;
    [ObservableProperty] private bool _isAhkActionRunning;
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _isUpdateApplying;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private string _currentProductVersion = "unknown";
    [ObservableProperty] private string _latestProductVersion = "unknown";
    public bool CanProbeDb() => !IsDbProbeRunning;
    public bool CanControlAhk() => !IsAhkActionRunning;

    public bool IsDbConnected => _dbMonitor.IsConnected;

    public string DbStatusTip
        => IsDbConnected ? "数据库：已连接" : "数据库：已断开";
    public string DbStatusText
        => IsDbConnected ? "已连接" : "已断开";
    public bool ShowDbBusyIcon => IsDbProbeRunning;
    public bool ShowDbConnectedIcon => IsDbConnected && !IsDbProbeRunning;
    public bool ShowDbDisconnectedIcon => !IsDbConnected && !IsDbProbeRunning;
    public bool IsSettingsPageActive => ActivePage is ISettingsPage;
    public bool IsAboutPageActive => ActivePage is IAboutPage;

    public bool IsAhkRunning => Injector.IsRunning;

    public string AhkStatusText
        => IsAhkRunning ? "运行中" : "未启动";

    public string UpdateStatusTip
        => HasUpdateAvailable
            ? $"发现新版本：{LatestProductVersion}（当前 {CurrentProductVersion}）"
            : "应用更新：当前已是最新版本";

    public string AppProductVersionText
    {
        get
        {
            var productVersion = _releaseVersion.Current.ProductVersion;
            return string.IsNullOrWhiteSpace(productVersion) ||
                   string.Equals(productVersion, "unknown", StringComparison.OrdinalIgnoreCase)
                ? "PacToolkits"
                : $"PacToolkits v{productVersion}";
        }
    }

    public string AppBuildChannelText
    {
        get
        {
            var channel = _releaseVersion.Current.BuildChannel;
            return string.IsNullOrWhiteSpace(channel) ||
                   string.Equals(channel, "unknown", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : channel;
        }
    }

    public string AppCopyrightDisplayText => "PacmanDoh · 2026";

    partial void OnHasUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(UpdateStatusTip));
    partial void OnCurrentProductVersionChanged(string value) => OnPropertyChanged(nameof(UpdateStatusTip));
    partial void OnLatestProductVersionChanged(string value) => OnPropertyChanged(nameof(UpdateStatusTip));

    private void RaiseDbStateChanged()
    {
        OnPropertyChanged(nameof(IsDbConnected));
        OnPropertyChanged(nameof(DbStatusTip));
        OnPropertyChanged(nameof(DbStatusText));
        OnPropertyChanged(nameof(ShowDbConnectedIcon));
        OnPropertyChanged(nameof(ShowDbDisconnectedIcon));
    }

    partial void OnIsDbProbeRunningChanged(bool value)
    {
        TryReconnectDbCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowDbBusyIcon));
        OnPropertyChanged(nameof(ShowDbConnectedIcon));
        OnPropertyChanged(nameof(ShowDbDisconnectedIcon));
    }

    private void RaiseAhkStateChanged()
    {
        OnPropertyChanged(nameof(IsAhkRunning));
        OnPropertyChanged(nameof(AhkStatusText));
    }

    private static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action, DispatcherPriority.Background);

    private static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
        => await UiThreadHelper.RunOnUiAsync(action, priority);

    private static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action, DispatcherPriority.Background);

    private static void PostOnUi(Action action, DispatcherPriority priority)
        => UiThreadHelper.PostOnUi(action, priority);

    private ITopBarActions? ActiveTopBar => ActivePage;

    public System.Windows.Input.ICommand? TopRefreshCommand => ActiveTopBar?.RefreshCommand;
    public System.Windows.Input.ICommand? TopImportCommand => ActiveTopBar?.ImportCommand;
    public System.Windows.Input.ICommand? TopExportCommand => ActiveTopBar?.ExportCommand;

    public bool CanTopRefresh => TopRefreshCommand?.CanExecute(null) == true;
    public bool CanTopImport => TopImportCommand?.CanExecute(null) == true;
    public bool CanTopExport => TopExportCommand?.CanExecute(null) == true;

    public string? TopRefreshTip => ActiveTopBar?.RefreshTip;
    public string? TopImportTip => ActiveTopBar?.ImportTip;
    public string? TopExportTip => ActiveTopBar?.ExportTip;

    [RelayCommand]
    private void RefreshActivePage()
    {
        if (ShouldSkipTrigger("main.top.refresh", 300))
            return;

        if (IsDbProbeRunning)
        {
            _toasts.Info("刷新", "数据库初始化进行中，请稍候");
            return;
        }

        var cmd = TopRefreshCommand;
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (ShouldSkipTrigger("main.top.update.check", 450))
            return;

        await CheckAndPromptUpdateAsync(showNoUpdateToast: true, startupMode: false).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenUpdateCenterAsync()
    {
        if (ShouldSkipTrigger("main.top.update.open", 450))
            return;

        if (IsUpdateApplying)
        {
            _toasts.Info("应用更新", "更新正在处理中，请稍候");
            return;
        }

        await ApplyUpdateFlowAsync().ConfigureAwait(false);
    }

    public MainWindowViewModel(
        IEnumerable<AppPageBase> pages,
        PageNavigationService nav,
        IToastService toasts,
        IDialogService dialogs,
        IAppConfigStore appConfigStore,
        IDbConfigService dbConfig,
        ISukiToastManager toastManager,
        ISukiDialogManager dialogManager,
        IDbConnectionMonitorService dbMonitor,
        IDbSchemaVersionService dbSchemaVersion,
        IDbSchemaMigrationService dbSchemaMigration,
        IChangeWatermarkService changeWatermark,
        IAgentManager agentManager,
        IReleaseVersionService releaseVersion,
        IAppStartupStateService startupState,
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IUpdateUiFlowService updateUiFlow,
        IAppLogger logger)
    {
        _toasts = toasts;
        _dialogs = dialogs;
        _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _dbMonitor = dbMonitor ?? throw new ArgumentNullException(nameof(dbMonitor));
        _dbSchemaVersion = dbSchemaVersion ?? throw new ArgumentNullException(nameof(dbSchemaVersion));
        _dbSchemaMigration = dbSchemaMigration ?? throw new ArgumentNullException(nameof(dbSchemaMigration));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
        _releaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
        _startupState = startupState ?? throw new ArgumentNullException(nameof(startupState));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
        _updateUiFlow = updateUiFlow ?? throw new ArgumentNullException(nameof(updateUiFlow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));


        _dbConfig.Applied += OnDbConfigAppliedEvent;

        ToastManager = toastManager;
        DialogManager = dialogManager;

        _configPath = _dbConfig.ConfigPath;
        _configDir = Path.GetDirectoryName(_configPath) ?? string.Empty;
        _configFile = Path.GetFileName(_configPath);

        var ordered = pages.OrderBy(p => p.Index).ToList();

        Pages = new AvaloniaList<AppPageBase>(ordered);
        SidebarPages = ordered.Where(p => p.ShowInSidebar).ToList();
        _pageByType = ordered.ToDictionary(p => p.GetType(), p => p);
        _settingsPage = ordered.FirstOrDefault(p => p is ISettingsPage);
        _aboutPage = ordered.FirstOrDefault(p => p is IAboutPage);

        _nav.NavigationRequested += OnNavigationRequested;

        ActivePage = SidebarPages.FirstOrDefault() ?? ordered.FirstOrDefault();
        SyncThemeState();

        _dbMonitor.ConnectionFailed += ShowDbConnectionFailed;
        _dbMonitor.Disconnected += ShowDbDisconnected;
        _dbMonitor.Reconnected += ShowDbReconnectedInfo;
        _dbMonitor.Reconnected += OnDbReconnectedRefreshSettingsSchema;
        _dbMonitor.Reconnected += OnDbReconnectedEnsureSchemaUpToDate;
        _changeWatermark.TopicChanged += OnWatermarkTopicChanged;

        _dbMonitor.Reconnected += ScheduleAutoRefresh;
        _dbMonitor.Disconnected += ScheduleAutoRefresh;
        Injector.StatusChanged += OnAhkStatusChanged;
        _updates.Changed += OnUpdateChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;

        CurrentProductVersion = _updates.CurrentVersion;
        LatestProductVersion = _updates.LatestVersion;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;

        _ = CheckConfigOnStartupAsync();
        StartConfigWatcher();
        RaiseAhkStateChanged();
        _ = InitializeAfterStartupChecksAsync();
        _logger.Info("MainWindowVM", "main.init", "Main window initialized");
    }

    private async Task InitializeAfterStartupChecksAsync()
    {
        try
        {
            await CheckDatabaseOnStartupAsync().ConfigureAwait(false);
            await RefreshSettingsSchemaStatusAsync("startup_postcheck").ConfigureAwait(false);
            MarkDirtyByType<MsfxLinkViewModel>();

            // Keep DB monitor/watermark loops alive whenever config exists, even if startup probe fails.
            // This enables automatic recovery after DB comes back online.
            if (File.Exists(_configPath))
            {
                _dbMonitor.Start();
                _changeWatermark.Start();
                StartDbStateBootstrap();
            }

            _startupState.MarkDbInitCompleted();

            await EnsureAhkStartedOnStartupAsync().ConfigureAwait(false);
            await CheckUpdatesOnStartupAsync().ConfigureAwait(false);
            RestartUpdatePolling();
        }
        finally
        {
            _startupState.MarkDbInitCompleted();
        }
    }

    private async Task EnsureAhkStartedOnStartupAsync()
    {
        if (Injector.IsRunning)
            return;

        try
        {
            var result = await Injector.StartOrRestartAsync().ConfigureAwait(false);
            if (!result.Ok && !result.SuppressToast)
            {
                _logger.Warn(
                    "MainWindowVM",
                    "ahk.startup_autostart.fail",
                    "Failed to auto-start automation toolkit on startup",
                    null,
                    new { result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "ahk.startup_autostart.exception", "Startup auto-start threw exception", ex);
        }
        finally
        {
            PostOnUi(RaiseAhkStateChanged);
        }
    }

    private void SyncThemeState()
    {
        var variant = _theme.ActiveBaseTheme;

        if (variant == ThemeVariant.Default)
        {
            variant = global::Avalonia.Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        }

        IsNightMode = variant == ThemeVariant.Dark;
    }

    private void StartDbStateBootstrap()
    {
        PostOnUi(RaiseDbStateChanged);
        _dbBootstrapCts?.Cancel();
        _dbBootstrapCts?.Dispose();
        _dbBootstrapCts = new CancellationTokenSource();
        _ = RunDbStateBootstrapAsync(_dbBootstrapCts.Token);
    }

    private async Task RunDbStateBootstrapAsync(CancellationToken ct)
    {
        for (var i = 0; i < 12; i++)
        {
            try
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            PostOnUi(RaiseDbStateChanged);

            if (_dbMonitor.IsConnected || ct.IsCancellationRequested)
                return;
        }
    }

    private void WireTopBarCommands(AppPageBase? newPage)
    {
        Detach(_lastRefreshCommand);
        Detach(_lastImportCommand);
        Detach(_lastExportCommand);

        _lastRefreshCommand = (newPage as ITopBarActions)?.RefreshCommand;
        _lastImportCommand = (newPage as ITopBarActions)?.ImportCommand;
        _lastExportCommand = (newPage as ITopBarActions)?.ExportCommand;

        Attach(_lastRefreshCommand);
        Attach(_lastImportCommand);
        Attach(_lastExportCommand);

        void Attach(System.Windows.Input.ICommand? cmd)
        {
            if (cmd is null) return;
            cmd.CanExecuteChanged += OnTopBarCanExecuteChanged;
        }

        void Detach(System.Windows.Input.ICommand? cmd)
        {
            if (cmd is null) return;
            cmd.CanExecuteChanged -= OnTopBarCanExecuteChanged;
        }
    }

    private void OnTopBarCanExecuteChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanTopRefresh));
        OnPropertyChanged(nameof(CanTopImport));
        OnPropertyChanged(nameof(CanTopExport));
    }


    partial void OnActivePageChanged(AppPageBase? value)
    {
        var previous = _activeLifecyclePage;
        if (!ReferenceEquals(previous, value))
        {
            _activeLifecyclePage = value;
            _pageLifecycleCts?.Cancel();
            _pageLifecycleCts?.Dispose();
            _pageLifecycleCts = new CancellationTokenSource();
            _ = RunPageLifecycleTransitionAsync(previous, value, _pageLifecycleCts.Token);
        }

        if (value is SettingsViewModel settingsPage)
        {
            settingsPage.ResetDraftFromCurrent();
            _ = settingsPage.RefreshDbSchemaStatusFromHostAsync("open_settings");
        }

        WireTopBarCommands(value);

        OnPropertyChanged(nameof(TopRefreshCommand));
        OnPropertyChanged(nameof(TopImportCommand));
        OnPropertyChanged(nameof(TopExportCommand));

        OnPropertyChanged(nameof(CanTopRefresh));
        OnPropertyChanged(nameof(CanTopImport));
        OnPropertyChanged(nameof(CanTopExport));

        OnPropertyChanged(nameof(TopRefreshTip));
        OnPropertyChanged(nameof(TopImportTip));
        OnPropertyChanged(nameof(TopExportTip));
        OnPropertyChanged(nameof(IsSettingsPageActive));
        OnPropertyChanged(nameof(IsAboutPageActive));

        TryRefreshDirtyActivePage();
    }

    private async Task RunPageLifecycleTransitionAsync(AppPageBase? previous, AppPageBase? current, CancellationToken ct)
    {
        try
        {
            if (previous is IPageLifecycleAware oldPage)
                await oldPage.OnPageDeactivatedAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.lifecycle.deactivate_fail", "Page deactivation failed", ex, new
            {
                page = previous?.GetType().Name
            });
        }

        try
        {
            if (current is IPageLifecycleAware newPage)
                await newPage.OnPageActivatedAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.lifecycle.activate_fail", "Page activation failed", ex, new
            {
                page = current?.GetType().Name
            });
        }
    }

    private void OnNavigationRequested(Type pageType)
    {
        if (_pageByType.TryGetValue(pageType, out var page))
            ActivePage = page;
    }

    [RelayCommand]
    private void ToggleBaseTheme()
    {
        if (ShouldSkipTrigger("main.theme.base", 220))
            return;

        var keepName = _theme.ActiveColorTheme?.DisplayName;

        _theme.SwitchBaseTheme();

        SyncThemeState();

        if (keepName is null) return;

        var match = Themes.FirstOrDefault(t => t.DisplayName == keepName);
        if (match is not null)
            _theme.ChangeColorTheme(match);
    }

    [RelayCommand]
    private void CycleThemeColor()
    {
        if (ShouldSkipTrigger("main.theme.color", 220))
            return;

        var themes = Themes;
        if (themes.Count == 0) return;

        var currentName = _theme.ActiveColorTheme?.DisplayName;
        var idx = -1;

        if (currentName is not null)
            idx = themes.ToList().FindIndex(t => t.DisplayName == currentName);

        var next = themes[(idx + 1) % themes.Count];
        _theme.ChangeColorTheme(next);
    }

    [RelayCommand(CanExecute = nameof(CanProbeDb))]
    private async Task TryReconnectDb()
    {
        if (ShouldSkipTrigger("top.db.probe", (int)TopActionDebounce.TotalMilliseconds))
            return;

        await RunOnUiAsync(() =>
        {
            IsDbProbeRunning = true;
            TryReconnectDbCommand.NotifyCanExecuteChanged();
        });

        var kind = _dbMonitor.IsConnected
            ? DbProbeKind.HealthCheck
            : DbProbeKind.Reconnect;

        DbProbeReport report;

        try
        {
            using var cts = new CancellationTokenSource(ProbeTimeout);
            report = await _dbMonitor.ProbeAsync(kind, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("MainWindowVM", "db.probe.timeout", "Database probe timed out");
            _toasts.Error("数据库", "操作超时：请检查网络/配置");
            return;
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "db.probe.error", "Database probe failed", ex);
            _toasts.Error("数据库", ex.Message);
            return;
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsDbProbeRunning = false;
                TryReconnectDbCommand.NotifyCanExecuteChanged();
            });
        }

        if (report.Success)
            _toasts.Success("数据库", kind == DbProbeKind.HealthCheck ? "健康检查通过" : "重连成功");
        else
        {
            _logger.Warn("MainWindowVM", "db.probe.unsuccessful", "Database probe finished with unsuccessful result", null, new
            {
                kind,
                report.Reason
            });
            _toasts.Error("数据库", report.Reason ?? "连接失败");
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAhk))]
    private async Task StartOrRestartAhk()
    {
        if (ShouldSkipTrigger("top.ahk.action", (int)TopActionDebounce.TotalMilliseconds))
            return;

        IsAhkActionRunning = true;
        StartOrRestartAhkCommand.NotifyCanExecuteChanged();

        try
        {
            if (Injector.IsRunning)
            {
                Injector.Reload();
                var running = Injector.IsRunning;
                if (running)
                    TryShowAhkTopToast(() => _toasts.Success("自动化套件", "健康检查通过：进程运行中"));
                else
                    TryShowAhkTopToast(() => _toasts.Error("自动化套件", "健康检查失败：未检测到进程运行"));
            }
            else
            {
                var result = await Injector.StartOrRestartAsync().ConfigureAwait(false);
                if (result.SuppressToast)
                    return;

                if (result.Ok)
                    TryShowAhkTopToast(() => _toasts.Success("自动化套件", result.Message));
                else
                    TryShowAhkTopToast(() => _toasts.Error("自动化套件", result.Message));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "ahk.top_action.error", "AHK top action failed", ex);
            TryShowAhkTopToast(() => _toasts.Error("自动化套件", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAhkActionRunning = false;
                StartOrRestartAhkCommand.NotifyCanExecuteChanged();
                RaiseAhkStateChanged();
            });
        }
    }

    private void TryShowAhkTopToast(Action show)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAhkTopToastAt < AhkTopToastDebounce)
            return;

        _lastAhkTopToastAt = now;
        show();
    }

    private async Task<bool> CheckDatabaseOnStartupAsync()
    {
        if (!File.Exists(_configPath))
            return false;

        _logger.Info("MainWindowVM", "db.startup_check.start", "Checking database connectivity on startup");
        var ok = await _dbConfig.TestConnectionAsync(_dbConfig.Current, CancellationToken.None).ConfigureAwait(false);
        if (!ok)
        {
            var openSettings = await _dialogs.Confirm(
                    "数据库未连接",
                    "检测到已存在配置文件，但无法连接数据库请前往 [设置] 重新配置并测试连接")
                .ConfigureAwait(false);

            if (openSettings)
                await RunOnUiAsync(OpenSettings);
            return false;
        }

        try
        {
            await RunOnUiAsync(() => IsDbProbeRunning = true);
            var currentAppVersion = NormalizeVersionForStamp(_releaseVersion.Current.ProductVersion);
            var state = await GetDbSchemaStartupStateAsync().ConfigureAwait(false);
            if (state.ShouldMigrate)
            {
                using var cts = new CancellationTokenSource(StartupDbMigrationTimeout);
                var migration = await _dbSchemaMigration
                    .EnsureUpToDateAsync(cts.Token, _releaseVersion.Current.DbSchemaVersion)
                    .ConfigureAwait(false);
                if (migration.HasChanges)
                    _toasts.Success("数据库结构更新", $"已应用 {migration.AppliedCount} 个迁移，当前版本 {migration.AfterVersion ?? "unknown"}");

                state = await GetDbSchemaStartupStateAsync().ConfigureAwait(false);
            }

            if (!state.Compatible)
            {
                _toasts.Error("数据库结构更新", $"自动更新后仍不兼容：{state.Message}");
                _logger.Error("MainWindowVM", "db.schema.incompatible.single_path.still_bad", "Schema incompatible after startup single-path migration", null, new
                {
                    state.UiMin,
                    state.AgentMin,
                    state.Target,
                    state.DbVersion,
                    state.SchemaOk,
                    state.Reason
                });
                return false;
            }

            await SaveDbMigrationStampAsync(currentAppVersion).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "db.startup_check.fail", "Database startup migration/check failed", ex);
            var openSettings = await _dialogs.Confirm(
                    "数据库初始化失败",
                    $"数据库初始化或结构升级失败：{ex.Message}\n请前往 [设置] 检查连接与权限后重试")
                .ConfigureAwait(false);

            if (openSettings)
                await RunOnUiAsync(OpenSettings);
            return false;
        }
        finally
        {
            await RunOnUiAsync(() => IsDbProbeRunning = false);
        }
    }

    private async Task SaveDbMigrationStampAsync(string currentAppVersion)
    {
        if (string.IsNullOrWhiteSpace(currentAppVersion) ||
            string.Equals(currentAppVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            return;

        var existingStamp = NormalizeVersionForStamp(_appConfigStore.Load().LastDbMigrationAppVersion);
        if (string.Equals(existingStamp, currentAppVersion, StringComparison.OrdinalIgnoreCase))
            return;

        await _appConfigStore.UpdateAsync(cfg =>
        {
            var currentStamp = NormalizeVersionForStamp(cfg.LastDbMigrationAppVersion);
            if (string.Equals(currentStamp, currentAppVersion, StringComparison.OrdinalIgnoreCase))
                return;

            cfg.LastDbMigrationAppVersion = currentAppVersion;
        }, CancellationToken.None).ConfigureAwait(false);
        _logger.Info("MainWindowVM", "db.startup_check.migrate.stamp.saved", "Saved DB migration app-version stamp", new
        {
            appVersion = currentAppVersion
        });
    }

    private static string NormalizeVersionForStamp(string? value)
        => (value ?? string.Empty).Trim();

    private async Task<DbSchemaStartupState> GetDbSchemaStartupStateAsync()
    {
        var version = _releaseVersion.Current;
        var target = DbSchemaCompat.NormalizeBound(version.DbSchemaVersion, version.DbSchemaVersion);
        var uiMin = DbSchemaCompat.NormalizeBound(version.UiMinDbSchema, version.DbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(version.AgentMinDbSchema, version.DbSchemaVersion);

        var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(CancellationToken.None).ConfigureAwait(false);
        var db = schema.value ?? string.Empty;
        var targetOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, target);
        var uiOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, uiMin);
        var agentOk = schema.ok && DbSchemaCompat.IsSemVerAtLeast(db, agentMin);
        var compatible = uiOk && agentOk;
        var shouldMigrate = !compatible;

        if (compatible)
            _logger.Info("MainWindowVM", "db.schema.ok", "Database schema version compatible", new
            {
                schema.value,
                target,
                uiMin,
                agentMin
            });
        else
            _logger.Warn("MainWindowVM", "db.schema.incompatible", "Database schema incompatible", null, new
            {
                target,
                uiMin,
                agentMin,
                schemaOk = schema.ok,
                schemaValue = schema.value,
                schema.reason,
                targetOk,
                uiOk,
                agentOk
            });

        var message = DbSchemaCompat.BuildIncompatibleMessage(
            schema.ok,
            schema.value,
            schema.reason,
            uiMin,
            agentMin);

        return new DbSchemaStartupState(
            Compatible: compatible,
            ShouldMigrate: shouldMigrate,
            Message: message,
            Target: target,
            UiMin: uiMin,
            AgentMin: agentMin,
            SchemaOk: schema.ok,
            DbVersion: schema.value,
            Reason: schema.reason);
    }

    private sealed record DbSchemaStartupState(
        bool Compatible,
        bool ShouldMigrate,
        string Message,
        string Target,
        string UiMin,
        string AgentMin,
        bool SchemaOk,
        string? DbVersion,
        string? Reason);

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updateSettings.Current.AutoCheckOnStartup)
            return;

        _logger.Info("MainWindowVM", "update.check.startup", "Auto checking updates on startup");
        await CheckAndPromptUpdateAsync(showNoUpdateToast: false, startupMode: true).ConfigureAwait(false);
    }

    private async Task CheckAndPromptUpdateAsync(bool showNoUpdateToast, bool startupMode)
    {
        if (_updates.IsChecking || IsUpdateApplying)
            return;

        await _updateUiFlow.CheckAndHandleAsync(
            showNoUpdateToast: showNoUpdateToast,
            startupMode: startupMode,
            applyNowAction: ApplyUpdateFlowAsync,
            ignoreVersionAction: IgnoreCurrentUpdateAsync,
            logScope: "MainWindowVM").ConfigureAwait(false);
    }

    private async Task ApplyUpdateFlowAsync()
    {
        if (IsUpdateApplying)
            return;

        IsUpdateApplying = true;
        try
        {
            await _updateUiFlow.ApplyUpdateFlowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "update.apply.error", "Update apply failed", ex);
            _toasts.Error("应用更新", ex.Message);
        }
        finally
        {
            IsUpdateApplying = false;
        }
    }

    private Task IgnoreCurrentUpdateAsync()
        => _updateUiFlow.IgnoreVersionAsync(LatestProductVersion);

    private void ShowDbConnectionFailed(string reason)
    {
        _dbEverDisconnected = true;

        PostOnUi(RaiseDbStateChanged);

        var now = DateTimeOffset.Now;

        if (now - _lastDbErrorToastAt < TimeSpan.FromSeconds(60)
            && string.Equals(_lastDbFailReason, reason, StringComparison.Ordinal))
            return;

        _lastDbErrorToastAt = now;
        _lastDbFailReason = reason;

        PostOnUi(() =>
        {
            _toasts.Error("数据连接失败", $"{reason}，请前往 [设置] 重新配置并测试连接");

        });
    }

    private void ShowDbDisconnected()
    {
        _dbEverDisconnected = true;
        _lastDbFailReason = null;

        PostOnUi(RaiseDbStateChanged);

        ScheduleAutoRefresh();
    }

    private void ShowDbReconnectedInfo()
    {
        PostOnUi(RaiseDbStateChanged);

        if (!_dbEverDisconnected)
            return;

        var now = DateTimeOffset.Now;

        if (now - _lastDbOkToastAt < TimeSpan.FromSeconds(15))
            return;

        _lastDbOkToastAt = now;
        _lastDbFailReason = null;

        PostOnUi(() =>
        {
            _toasts.Info("数据库连接恢复", "数据库连接已成功恢复");
        });
    }

    private void OnDbReconnectedRefreshSettingsSchema()
        => _ = RefreshSettingsSchemaStatusAsync("db_reconnected");

    private void OnDbReconnectedEnsureSchemaUpToDate()
        => _ = EnsureSchemaUpToDateOnReconnectAsync();

    private async Task EnsureSchemaUpToDateOnReconnectAsync()
    {
        if (Interlocked.Exchange(ref _dbReconnectMigrationRunning, 1) == 1)
            return;

        try
        {
            var state = await GetDbSchemaStartupStateAsync().ConfigureAwait(false);
            if (!state.ShouldMigrate)
            {
                await RefreshSettingsSchemaStatusAsync("db_reconnected").ConfigureAwait(false);
                return;
            }

            using var cts = new CancellationTokenSource(StartupDbMigrationTimeout);
            var migration = await _dbSchemaMigration
                .EnsureUpToDateAsync(cts.Token, _releaseVersion.Current.DbSchemaVersion)
                .ConfigureAwait(false);
            if (migration.HasChanges)
            {
                PostOnUi(() =>
                {
                    _toasts.Success("数据库结构更新", $"连接恢复后已自动应用 {migration.AppliedCount} 个迁移，当前版本 {migration.AfterVersion ?? "unknown"}");
                });
            }

            var currentAppVersion = NormalizeVersionForStamp(_releaseVersion.Current.ProductVersion);
            await SaveDbMigrationStampAsync(currentAppVersion).ConfigureAwait(false);
            await RefreshSettingsSchemaStatusAsync("db_reconnected_migrate").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "db.reconnected.migrate.fail", "Auto migration on DB reconnect failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _dbReconnectMigrationRunning, 0);
        }
    }

    private async Task RefreshSettingsSchemaStatusAsync(string source)
    {
        if (_settingsPage is not SettingsViewModel settingsPage)
            return;

        try
        {
            await settingsPage.RefreshDbSchemaStatusFromHostAsync(source).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "db.schema.status.refresh.fail", "Failed to refresh DB schema status for Settings page", ex, new
            {
                source
            });
        }
    }

    private void OnAhkStatusChanged()
    {
        PostOnUi(() =>
        {
            RaiseAhkStateChanged();
            StartOrRestartAhkCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnUpdateChanged()
    {
        PostOnUi(() =>
        {
            CurrentProductVersion = _updates.CurrentVersion;
            LatestProductVersion = _updates.LatestVersion;
            HasUpdateAvailable = _updates.HasUpdateAvailable;
            IsUpdateChecking = _updates.IsChecking;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SafeExecute(() => _dbConfig.Applied -= OnDbConfigAppliedEvent);
        SafeExecute(() => _nav.NavigationRequested -= OnNavigationRequested);
        SafeExecute(() => _dbMonitor.ConnectionFailed -= ShowDbConnectionFailed);
        SafeExecute(() => _dbMonitor.Disconnected -= ShowDbDisconnected);
        SafeExecute(() => _dbMonitor.Reconnected -= ShowDbReconnectedInfo);
        SafeExecute(() => _dbMonitor.Reconnected -= OnDbReconnectedRefreshSettingsSchema);
        SafeExecute(() => _dbMonitor.Reconnected -= OnDbReconnectedEnsureSchemaUpToDate);
        SafeExecute(() => _dbMonitor.Reconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _dbMonitor.Disconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _changeWatermark.TopicChanged -= OnWatermarkTopicChanged);
        SafeExecute(() => Injector.StatusChanged -= OnAhkStatusChanged);
        SafeExecute(() => _updates.Changed -= OnUpdateChanged);
        SafeExecute(() => _updateSettings.Changed -= OnUpdateSettingsChanged);

        WireTopBarCommands(null);

        if (_configWatcher is not null)
        {
            SafeExecute(() =>
            {
                _configWatcher.EnableRaisingEvents = false;
                _configWatcher.Changed -= OnConfigWatcherChanged;
                _configWatcher.Created -= OnConfigWatcherChanged;
                _configWatcher.Renamed -= OnConfigWatcherRenamed;
                _configWatcher.Dispose();
            });

            _configWatcher = null;
        }

        SafeExecute(() =>
        {
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
            _dbBootstrapCts?.Cancel();
            _dbBootstrapCts?.Dispose();
            _dbBootstrapCts = null;
            _updatePollCts?.Cancel();
            _updatePollCts?.Dispose();
            _updatePollCts = null;
            _pageLifecycleCts?.Cancel();
            _pageLifecycleCts?.Dispose();
            _pageLifecycleCts = null;
        });
    }

    private void SafeExecute(Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            _logger.Warn("MainWindowVM", "dispose.safe_execute_fail", "Dispose cleanup action failed", ex);
        }
    }
}
