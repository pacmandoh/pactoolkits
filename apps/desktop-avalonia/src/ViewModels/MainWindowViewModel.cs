using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Collections;
using global::Avalonia.Styling;
using global::Avalonia.Threading;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public ToastManager ToastManager { get; }
    public DialogManager DialogManager { get; }

    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionMonitorService _dbMonitor;
    private readonly IDatabaseAccessGuard _accessGuard;
    private readonly ILookupCatalogService _lookup;
    private readonly ISettingsService _settings;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly IAgentManager _agentManager;
    private IAgentRuntime Injector => _agentManager.GetRequired(AgentIds.InjectorAhk);
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAppStartupStateService _startupState;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IUpdateDesktopFlowService _updateDesktopFlow;
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
    private bool _isDbConnectivityKnown;
    private int _dbReconnectMigrationRunning;
    private bool _wasAccessGuardBlocked;
    private CancellationTokenSource? _schemaRecoveryCts;

    private readonly TimeSpan _autoRefreshDebounce = TimeSpan.FromMilliseconds(180);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _dbBootstrapCts;
    private CancellationTokenSource? _updatePollCts;
    private CancellationTokenSource? _pageLifecycleCts;
    private int _pageLifecycleGeneration;
    private readonly object _dirtyPagesGate = new();
    private readonly HashSet<AppPageBase> _dirtyPages = new();

    private readonly ThemeWatcher _themeWatcher;

    public IReadOnlyList<AppPageBase> WorkspacePages { get; }
    public IReadOnlyList<AppPageBase> SidebarPages { get; }

    public IReadOnlyList<ShellFunctionArea> FunctionAreas => ShellFunctionAreas.All;

    [ObservableProperty]
    private ShellFunctionArea _selectedFunctionArea = ShellFunctionAreas.Traceability;

    private IReadOnlyList<AppPageBase> _filteredSidebarPages = Array.Empty<AppPageBase>();

    public IReadOnlyList<AppPageBase> FilteredSidebarPages => _filteredSidebarPages;
    private readonly Dictionary<Type, AppPageBase> _pageByType;
    private readonly AppPageBase? _settingsPage;
    private readonly AppPageBase? _aboutPage;
    private AppPageBase? _activeLifecyclePage;
    private bool _disposed;

    private System.Windows.Input.ICommand? _lastRefreshCommand;
    private System.Windows.Input.ICommand? _lastImportCommand;
    private System.Windows.Input.ICommand? _lastExportCommand;
    private INotifyPropertyChanged? _topBarPageNotifier;


    [RelayCommand]
    private void OpenSettings()
    {
        if (ShouldSkipTrigger("main.nav.settings", 250))
        {
            return;
        }

        var page = _settingsPage;

        if (page is not null)
        {
            ActivePage = page;
        }
    }

    [RelayCommand]
    private void OpenAbout()
    {
        if (ShouldSkipTrigger("main.nav.about", 250))
        {
            return;
        }

        var page = _aboutPage;

        if (page is not null)
        {
            ActivePage = page;
        }
    }

    [RelayCommand]
    private void SelectFunctionArea(ShellFunctionArea? area)
    {
        if (area is null)
        {
            return;
        }

        SelectedFunctionArea = area;
    }

    [RelayCommand]
    private void NavigateSidebarPage(AppPageBase? page)
    {
        if (page is null || !page.IsEnabled)
        {
            return;
        }

        ActivePage = page;
    }

    [ObservableProperty] private AppPageBase? _activePage;
    [ObservableProperty] private ThemeMode _currentTheme = ThemeMode.Dark;
    [ObservableProperty] private bool _isSidebarExpanded = true;
    [ObservableProperty] private string? _activePageRoute;
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

    public string DbStatusText
        => IsDbConnected ? "已连接" : "已断开";

    public string ShellDatabaseItemText
        => IsDbProbeRunning ? "数据库：检测中…"
        : IsDbConnected ? "数据库：已连接"
        : "数据库：未连接";

    public string ShellAgentItemText => $"Agent：{AhkStatusText}";

    public string ShellActivePageText => ActivePage?.DisplayName ?? "就绪";

    public bool ShowShellAccessGuardItem => _accessGuard.IsBlocked;

    public string ShellAccessGuardItemText
        => string.IsNullOrWhiteSpace(_accessGuard.BlockReason)
            ? "配置未完成"
            : _accessGuard.BlockReason!;

    public string ShellVersionText
    {
        get
        {
            var productVersion = _releaseVersion.Current.ProductVersion;
            if (string.IsNullOrWhiteSpace(productVersion) ||
                string.Equals(productVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return "PacToolkits";
            }

            var channel = AppBuildChannelText;
            return string.IsNullOrWhiteSpace(channel)
                ? $"v{productVersion}"
                : $"v{productVersion} · {channel}";
        }
    }

    public string ShellVersionBarText
        => IsUpdateChecking ? "检查更新…" : ShellVersionText;

    public bool ShowShellConnectivityBanner { get; private set; }
    public string ShellConnectivityBannerTitle { get; private set; } = string.Empty;
    public string ShellConnectivityBannerMessage { get; private set; } = string.Empty;
    public bool ShowShellConnectivityBannerAction { get; private set; }
    public bool ShellConnectivityBannerIsError { get; private set; }
    public bool ShellConnectivityBannerIsWarning { get; private set; }
    public bool ShellConnectivityBannerIsInfo { get; private set; }

    public bool IsSettingsPageActive => ActivePage is ISettingsPage;
    public bool IsAboutPageActive => ActivePage is IAboutPage;

    public bool IsAhkRunning => Injector.IsRunning;

    public string AhkStatusText
        => IsAhkRunning ? "运行中" : "未启动";

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

    partial void OnCurrentProductVersionChanged(string value)
    {
        OnPropertyChanged(nameof(ShellVersionText));
        OnPropertyChanged(nameof(ShellVersionBarText));
    }

    partial void OnIsUpdateCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShellVersionBarText));
    }

    private void MarkDbConnectivityKnown()
    {
        if (_isDbConnectivityKnown)
        {
            return;
        }

        _isDbConnectivityKnown = true;
    }

    private void RaiseDbStateChanged()
    {
        OnPropertyChanged(nameof(IsDbConnected));
        OnPropertyChanged(nameof(DbStatusText));
        OnPropertyChanged(nameof(ShellDatabaseItemText));
        RaiseShellConnectivityChanged();
    }

    private void RaiseShellStatusItemsChanged()
    {
        OnPropertyChanged(nameof(ShellDatabaseItemText));
        OnPropertyChanged(nameof(ShellAgentItemText));
        OnPropertyChanged(nameof(ShellActivePageText));
        OnPropertyChanged(nameof(ShowShellAccessGuardItem));
        OnPropertyChanged(nameof(ShellAccessGuardItemText));
        OnPropertyChanged(nameof(ShellVersionText));
        OnPropertyChanged(nameof(ShellVersionBarText));
    }

    private void RaiseShellConnectivityChanged()
    {
        var wasBlocked = _wasAccessGuardBlocked;
        var banner = ShellConnectivityBannerFactory.Create(
            IsDbConnected,
            _isDbConnectivityKnown,
            _accessGuard);
        var isBlocked = _accessGuard.IsBlocked;
        _wasAccessGuardBlocked = isBlocked;

        if (isBlocked)
        {
            _lookup.InvalidateDrugCatalog();
        }

        ShowShellConnectivityBanner = banner.IsVisible;
        ShellConnectivityBannerTitle = banner.Title;
        ShellConnectivityBannerMessage = banner.Message;
        ShowShellConnectivityBannerAction = banner.ShowOpenSettings;
        ShellConnectivityBannerIsError = banner.Severity == ShellConnectivitySeverity.Error;
        ShellConnectivityBannerIsWarning = banner.Severity == ShellConnectivitySeverity.Warning;
        ShellConnectivityBannerIsInfo = banner.Severity == ShellConnectivitySeverity.Info;

        OnPropertyChanged(nameof(ShowShellConnectivityBanner));
        OnPropertyChanged(nameof(ShellConnectivityBannerTitle));
        OnPropertyChanged(nameof(ShellConnectivityBannerMessage));
        OnPropertyChanged(nameof(ShowShellConnectivityBannerAction));
        OnPropertyChanged(nameof(ShellConnectivityBannerIsError));
        OnPropertyChanged(nameof(ShellConnectivityBannerIsWarning));
        OnPropertyChanged(nameof(ShellConnectivityBannerIsInfo));
        SyncAllPagesAvailability();

        if (wasBlocked && !isBlocked)
        {
            ScheduleAutoRefresh();
        }

        ManageSchemaRecoveryPolling(isBlocked && IsDbConnected && !IsDbProbeRunning);
        RaiseShellStatusItemsChanged();
    }

    private void ManageSchemaRecoveryPolling(bool shouldPoll)
    {
        if (!shouldPoll)
        {
            _schemaRecoveryCts?.Cancel();
            _schemaRecoveryCts?.Dispose();
            _schemaRecoveryCts = null;
            return;
        }

        if (_schemaRecoveryCts is not null)
        {
            return;
        }

        _schemaRecoveryCts = new CancellationTokenSource();
        _ = RunSchemaRecoveryPollingAsync(_schemaRecoveryCts.Token);
    }

    private async Task RunSchemaRecoveryPollingAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested || !_accessGuard.IsBlocked || !IsDbConnected)
                {
                    return;
                }

                await RefreshSettingsSchemaStatusAsync("guard_recovery_poll").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "db.schema.recovery_poll.fail", "Schema recovery polling failed", ex);
        }
    }

    private void SyncAllPagesAvailability()
    {
        foreach (var page in WorkspacePages)
        {
            page.SyncPageAvailabilityFromEnvironment();
        }
    }

    partial void OnIsDbProbeRunningChanged(bool value)
    {
        TryReconnectDbCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShellDatabaseItemText));
    }

    partial void OnIsAhkActionRunningChanged(bool value)
    {
        StartOrRestartAhkCommand.NotifyCanExecuteChanged();
    }

    private void RaiseAhkStateChanged()
    {
        OnPropertyChanged(nameof(IsAhkRunning));
        OnPropertyChanged(nameof(AhkStatusText));
        OnPropertyChanged(nameof(ShellAgentItemText));
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
    private DashboardViewModel? _dashboardFilterBarSource;

    public bool IsDashboardPageActive => ActivePage is DashboardViewModel;

    public bool IsDashboardFilterBarVisible
    {
        get => ActivePage is DashboardViewModel dashboard && dashboard.IsFilterBarVisible;
        set
        {
            if (ActivePage is DashboardViewModel dashboard && dashboard.IsFilterBarVisible != value)
            {
                dashboard.IsFilterBarVisible = value;
            }
        }
    }

    public System.Windows.Input.ICommand? TopRefreshCommand => ActiveTopBar?.RefreshCommand;
    public System.Windows.Input.ICommand? TopImportCommand => ActiveTopBar?.ImportCommand;
    public System.Windows.Input.ICommand? TopExportCommand => ActiveTopBar?.ExportCommand;

    public bool ShowTopRefresh => TopRefreshCommand is not null;
    public bool ShowTopImport => TopImportCommand is not null;
    public bool ShowTopExport => TopExportCommand is not null;

    public bool CanTopRefresh => TopRefreshCommand?.CanExecute(null) == true;
    public bool CanTopImport => TopImportCommand?.CanExecute(null) == true;
    public bool CanTopExport => TopExportCommand?.CanExecute(null) == true;

    [RelayCommand]
    private void RefreshActivePage()
    {
        if (ShouldSkipTrigger("main.top.refresh", 300))
        {
            return;
        }

        if (IsDbProbeRunning)
        {
            _toasts.Info("刷新", "数据库初始化进行中，请稍候");
            return;
        }

        var cmd = TopRefreshCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (ShouldSkipTrigger("main.top.update.check", 450))
        {
            return;
        }

        await CheckAndPromptUpdateAsync(showNoUpdateToast: true, startupMode: false).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenUpdateCenterAsync()
    {
        if (ShouldSkipTrigger("main.top.update.open", 450))
        {
            return;
        }

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
        ToastManager toastManager,
        DialogManager dialogManager,
        IDbConnectionMonitorService dbMonitor,
        IDatabaseAccessGuard accessGuard,
        ILookupCatalogService lookup,
        ISettingsService settings,
        IChangeWatermarkService changeWatermark,
        IAgentManager agentManager,
        IReleaseVersionService releaseVersion,
        IAppStartupStateService startupState,
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IUpdateDesktopFlowService updateDesktopFlow,
        IAppLogger logger)
    {
        _toasts = toasts;
        _dialogs = dialogs;
        _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _dbMonitor = dbMonitor ?? throw new ArgumentNullException(nameof(dbMonitor));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
        _releaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
        _startupState = startupState ?? throw new ArgumentNullException(nameof(startupState));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
        _updateDesktopFlow = updateDesktopFlow ?? throw new ArgumentNullException(nameof(updateDesktopFlow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));


        _dbConfig.Applied += OnDbConfigAppliedEvent;

        ToastManager = toastManager;
        DialogManager = dialogManager;

        _themeWatcher = new ThemeWatcher(global::Avalonia.Application.Current!);
        _themeWatcher.Initialize();

        _configPath = _dbConfig.ConfigPath;
        _configDir = Path.GetDirectoryName(_configPath) ?? string.Empty;
        _configFile = Path.GetFileName(_configPath);

        var ordered = pages.OrderBy(p => p.Index).ToList();

        WorkspacePages = new AvaloniaList<AppPageBase>(ordered);
        SidebarPages = ordered.Where(p => p.ShowInSidebar).ToList();
        _pageByType = ordered.ToDictionary(p => p.GetType(), p => p);
        _settingsPage = ordered.FirstOrDefault(p => p is ISettingsPage);
        _aboutPage = ordered.FirstOrDefault(p => p is IAboutPage);

        _nav.NavigationRequested += OnNavigationRequested;

        ActivePage = SidebarPages.FirstOrDefault() ?? ordered.FirstOrDefault();
        ActivePageRoute = ActivePage?.SidebarRoute;
        SelectedFunctionArea = ShellFunctionAreas.Resolve(ActivePage?.FunctionAreaId);
        RebuildFilteredSidebarPages();
        CurrentTheme = ResolveThemeMode(global::Avalonia.Application.Current?.RequestedThemeVariant);

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
        _wasAccessGuardBlocked = _accessGuard.IsBlocked;
        RaiseShellConnectivityChanged();
        _ = InitializeAfterStartupChecksAsync();
        _logger.Info("MainWindowVM", "main.init", "Main window initialized");
    }

    private async Task InitializeAfterStartupChecksAsync()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                _dbMonitor.Start();
            }

            await CheckDatabaseOnStartupAsync().ConfigureAwait(false);
            await RefreshSettingsSchemaStatusAsync("startup_postcheck").ConfigureAwait(false);
            MarkDirtyByType<MsfxLinkViewModel>();

            if (File.Exists(_configPath))
            {
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
        if (!Injector.IsEnabled || Injector.IsRunning)
        {
            return;
        }

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

    private static ThemeMode ResolveThemeMode(ThemeVariant? variant)
    {
        if (variant == ThemeVariant.Dark)
        {
            return ThemeMode.Dark;
        }

        if (variant == ThemeVariant.Light)
        {
            return ThemeMode.Light;
        }

        return ThemeMode.System;
    }

    [RelayCommand]
    private void SwitchTheme()
    {
        CurrentTheme = CurrentTheme switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        _themeWatcher.SwitchTheme(CurrentTheme);
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
            {
                return;
            }
        }
    }

    private void WireDashboardFilterBar(AppPageBase? page)
    {
        _dashboardFilterBarSource?.PropertyChanged -= OnDashboardFilterBarPropertyChanged;

        _dashboardFilterBarSource = page as DashboardViewModel;

        _dashboardFilterBarSource?.PropertyChanged += OnDashboardFilterBarPropertyChanged;
    }

    private void OnDashboardFilterBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.IsFilterBarVisible))
        {
            OnPropertyChanged(nameof(IsDashboardFilterBarVisible));
        }
    }

    private void WireTopBarCommands(AppPageBase? newPage)
    {
        Detach(_lastRefreshCommand);
        Detach(_lastImportCommand);
        Detach(_lastExportCommand);

        _topBarPageNotifier?.PropertyChanged -= OnActivePageTopBarPropertyChanged;
        _topBarPageNotifier = null;

        _lastRefreshCommand = (newPage as ITopBarActions)?.RefreshCommand;
        _lastImportCommand = (newPage as ITopBarActions)?.ImportCommand;
        _lastExportCommand = (newPage as ITopBarActions)?.ExportCommand;

        Attach(_lastRefreshCommand);
        Attach(_lastImportCommand);
        Attach(_lastExportCommand);

        if (newPage is INotifyPropertyChanged notifier)
        {
            _topBarPageNotifier = notifier;
            _topBarPageNotifier.PropertyChanged += OnActivePageTopBarPropertyChanged;
        }

        RaiseTopBarBindings();

        void Attach(System.Windows.Input.ICommand? cmd)
        {
            if (cmd is null)
            {
                return;
            }

            cmd.CanExecuteChanged += OnTopBarCanExecuteChanged;
        }

        void Detach(System.Windows.Input.ICommand? cmd)
        {
            if (cmd is null)
            {
                return;
            }

            cmd.CanExecuteChanged -= OnTopBarCanExecuteChanged;
        }
    }

    private void OnActivePageTopBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ITopBarActions.RefreshCommand)
            or nameof(ITopBarActions.ImportCommand)
            or nameof(ITopBarActions.ExportCommand)))
        {
            return;
        }

        WireTopBarCommands(ActivePage);
    }

    private void OnTopBarCanExecuteChanged(object? sender, EventArgs e)
        => RaiseTopBarCanExecuteBindings();

    private void RaiseTopBarBindings()
    {
        OnPropertyChanged(nameof(TopRefreshCommand));
        OnPropertyChanged(nameof(TopImportCommand));
        OnPropertyChanged(nameof(TopExportCommand));
        RaiseTopBarVisibilityBindings();
        RaiseTopBarCanExecuteBindings();
    }

    private void RaiseTopBarVisibilityBindings()
    {
        OnPropertyChanged(nameof(ShowTopRefresh));
        OnPropertyChanged(nameof(ShowTopImport));
        OnPropertyChanged(nameof(ShowTopExport));
    }

    private void RaiseTopBarCanExecuteBindings()
    {
        OnPropertyChanged(nameof(CanTopRefresh));
        OnPropertyChanged(nameof(CanTopImport));
        OnPropertyChanged(nameof(CanTopExport));
    }


    partial void OnSelectedFunctionAreaChanged(ShellFunctionArea value)
    {
        RebuildFilteredSidebarPages();

        if (ActivePage is not null
            && string.Equals(ActivePage.FunctionAreaId, value.Id, StringComparison.Ordinal))
        {
            return;
        }

        var firstInArea = SidebarPages.FirstOrDefault(page =>
            string.Equals(page.FunctionAreaId, value.Id, StringComparison.Ordinal));

        if (firstInArea is not null)
        {
            ActivePage = firstInArea;
        }
    }

    private void RebuildFilteredSidebarPages()
    {
        _filteredSidebarPages = FilterSidebarPages(SelectedFunctionArea);
        OnPropertyChanged(nameof(FilteredSidebarPages));
    }

    private IReadOnlyList<AppPageBase> FilterSidebarPages(ShellFunctionArea? area)
    {
        var areaId = area?.Id ?? ShellFunctionAreas.TraceabilityId;
        return SidebarPages
            .Where(page => string.Equals(page.FunctionAreaId, areaId, StringComparison.Ordinal))
            .ToList();
    }

    partial void OnActivePageChanged(AppPageBase? value)
    {
        if (!string.Equals(ActivePageRoute, value?.SidebarRoute, StringComparison.Ordinal))
        {
            ActivePageRoute = value?.SidebarRoute;
        }

        var previous = _activeLifecyclePage;
        if (!ReferenceEquals(previous, value))
        {
            _activeLifecyclePage = value;
            _pageLifecycleCts?.Cancel();
            _pageLifecycleCts?.Dispose();
            _pageLifecycleCts = new CancellationTokenSource();
            var generation = ++_pageLifecycleGeneration;
            _ = RunPageLifecycleTransitionAsync(previous, value, generation, _pageLifecycleCts.Token);
        }

        value?.SyncPageAvailabilityFromEnvironment();

        if (value is SettingsViewModel settingsPage)
        {
            settingsPage.ResetDraftFromCurrent();
            _ = settingsPage.RefreshDbSchemaStatusFromHostAsync("open_settings");
        }

        if (value is not null
            && value.ShowInSidebar
            && !string.Equals(SelectedFunctionArea.Id, value.FunctionAreaId, StringComparison.Ordinal))
        {
            SelectedFunctionArea = ShellFunctionAreas.Resolve(value.FunctionAreaId);
        }

        WireTopBarCommands(value);
        WireDashboardFilterBar(value);

        OnPropertyChanged(nameof(IsSettingsPageActive));
        OnPropertyChanged(nameof(IsAboutPageActive));
        OnPropertyChanged(nameof(IsDashboardPageActive));
        OnPropertyChanged(nameof(IsDashboardFilterBarVisible));
        RaiseTopBarVisibilityBindings();
        RaiseShellStatusItemsChanged();

        TryRefreshDirtyActivePage();
    }

    partial void OnActivePageRouteChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var page = SidebarPages.FirstOrDefault(p =>
            string.Equals(p.SidebarRoute, value, StringComparison.Ordinal));

        if (page is not null && !ReferenceEquals(ActivePage, page) && page.IsEnabled)
        {
            ActivePage = page;
        }
    }

    private async Task RunPageLifecycleTransitionAsync(
        AppPageBase? previous,
        AppPageBase? current,
        int generation,
        CancellationToken ct)
    {
        try
        {
            if (previous is IPageLifecycleAware oldPage)
            {
                await oldPage.OnPageDeactivatedAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

        if (ct.IsCancellationRequested || generation != _pageLifecycleGeneration)
        {
            return;
        }

        try
        {
            if (current is IPageLifecycleAware newPage)
            {
                await newPage.OnPageActivatedAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.lifecycle.activate_fail", "Page activation failed", ex, new
            {
                page = current?.GetType().Name
            });
        }

        if (generation != _pageLifecycleGeneration || current is null)
        {
            return;
        }

        await RunOnUiAsync(() => current.SyncPageAvailabilityFromEnvironment(), DispatcherPriority.Loaded);
    }

    private void OnNavigationRequested(Type pageType)
    {
        if (_pageByType.TryGetValue(pageType, out var page))
        {
            ActivePage = page;
        }
    }

    [RelayCommand(CanExecute = nameof(CanProbeDb))]
    private async Task TryReconnectDb()
    {
        if (ShouldSkipTrigger("top.db.probe", (int)TopActionDebounce.TotalMilliseconds))
        {
            return;
        }

        _dbMonitor.Start();

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
                MarkDbConnectivityKnown();
                RaiseDbStateChanged();
            });
        }

        if (report.Success)
        {
            _toasts.Success("数据库", kind == DbProbeKind.HealthCheck ? "健康检查通过" : "重连成功");
        }
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
        {
            return;
        }

        IsAhkActionRunning = true;
        StartOrRestartAhkCommand.NotifyCanExecuteChanged();

        try
        {
            if (Injector.IsRunning)
            {
                Injector.Reload();
                var running = Injector.IsRunning;
                if (running)
                {
                    TryShowAhkTopToast(() => _toasts.Success("自动化套件", "健康检查通过：进程运行中"));
                }
                else
                {
                    TryShowAhkTopToast(() => _toasts.Error("自动化套件", "健康检查失败：未检测到进程运行"));
                }
            }
            else
            {
                var result = await Injector.StartOrRestartAsync().ConfigureAwait(false);
                if (result.SuppressToast)
                {
                    return;
                }

                if (result.Ok)
                {
                    TryShowAhkTopToast(() => _toasts.Success("自动化套件", result.Message));
                }
                else
                {
                    TryShowAhkTopToast(() => _toasts.Error("自动化套件", result.Message));
                }
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
        {
            return;
        }

        _lastAhkTopToastAt = now;
        show();
    }

    private async Task<bool> CheckDatabaseOnStartupAsync()
    {
        if (!File.Exists(_configPath))
        {
            return false;
        }

        _logger.Info("MainWindowVM", "db.startup_check.start", "Checking database connectivity on startup");
        var ok = await _dbConfig.TestConnectionAsync(_dbConfig.Current, CancellationToken.None).ConfigureAwait(false);
        if (!ok)
        {
            _logger.Warn("MainWindowVM", "db.startup_check.fail", "Database connection test failed on startup; shell banner will show status");
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
                var (migrationOk, summary) = await _settings
                    .EnsureSchemaUpToDateAsync(
                        BuildSchemaContext(),
                        DatabaseMigrationTrigger.Startup,
                        ct: cts.Token)
                    .ConfigureAwait(false);
                if (!migrationOk)
                {
                    _logger.Warn("MainWindowVM", "db.startup_check.migrate.blocked",
                        "Startup migration blocked by policy", null, new { summary });
                }
                else if (TryParseAppliedCount(summary, out var applied) && applied > 0)
                {
                    _toasts.Success("数据库结构更新", $"已应用 {applied} 个迁移");
                }

                state = await GetDbSchemaStartupStateAsync().ConfigureAwait(false);
            }

            if (!state.Compatible)
            {
                _toasts.Error("数据库版本不兼容", state.Message);
                var logContext = new
                {
                    desktopMin = state.DesktopMin,
                    desktopMax = state.DesktopMax,
                    agentMin = state.AgentMin,
                    agentMax = state.AgentMax,
                    target = state.Target,
                    dbVersion = state.DbVersion,
                    schemaOk = state.SchemaOk,
                    compatibility = state.Compatibility,
                    reason = state.Reason
                };

                if (state.SchemaOk && string.Equals(state.Compatibility, "BelowMinimum", StringComparison.Ordinal))
                {
                    _logger.Warn("MainWindowVM", "db.schema.pending_migration.startup",
                        "Database schema below app minimum during startup; migration required before business access",
                        null,
                        logContext);
                }
                else
                {
                    _logger.Error("MainWindowVM", "db.schema.incompatible.startup",
                        "Database schema incompatible during startup",
                        null,
                        logContext);
                }

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
            {
                await RunOnUiAsync(OpenSettings);
            }

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
        {
            return;
        }

        var existingStamp = NormalizeVersionForStamp(_appConfigStore.Load().LastDbMigrationAppVersion);
        if (string.Equals(existingStamp, currentAppVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _appConfigStore.UpdateAsync(cfg =>
        {
            var currentStamp = NormalizeVersionForStamp(cfg.LastDbMigrationAppVersion);
            if (string.Equals(currentStamp, currentAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            cfg.LastDbMigrationAppVersion = currentAppVersion;
        }, CancellationToken.None).ConfigureAwait(false);
        _logger.Info("MainWindowVM", "db.startup_check.migrate.stamp.saved", "Saved DB migration app-version stamp", new
        {
            appVersion = currentAppVersion
        });
    }

    private static string NormalizeVersionForStamp(string? value)
        => (value ?? string.Empty).Trim();

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

    private static bool TryParseAppliedCount(string summary, out int applied)
    {
        applied = 0;
        const string marker = "applied=";
        var idx = summary.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        var start = idx + marker.Length;
        var end = start;
        while (end < summary.Length && char.IsDigit(summary[end]))
        {
            end++;
        }

        return end > start && int.TryParse(summary[start..end], out applied);
    }

    private async Task<DbSchemaStartupState> GetDbSchemaStartupStateAsync(
        DatabaseMigrationTrigger trigger = DatabaseMigrationTrigger.Startup)
    {
        var version = _releaseVersion.Current;
        var context = BuildSchemaContext();
        var uiMin = DbSchemaCompat.NormalizeBound(version.UiMinDbSchema, version.DbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(version.UiMaxDbSchema, version.DbSchemaVersion);
        var agentMin = DbSchemaCompat.NormalizeBound(version.AgentMinDbSchema, version.DbSchemaVersion);
        var agentMax = DbSchemaCompat.NormalizeBound(version.AgentMaxDbSchema, version.DbSchemaVersion);
        var target = DbSchemaCompat.NormalizeBound(version.DbSchemaVersion, version.DbSchemaVersion);

        var snapshot = await _settings
            .ReadSchemaStatusAsync(context, trigger, CancellationToken.None)
            .ConfigureAwait(false);

        if (snapshot.Satisfied)
        {
            _logger.Info("MainWindowVM", "db.schema.ok", "Database schema version compatible", new
            {
                schemaValue = snapshot.CurrentVersion,
                target,
                desktopMin = uiMin,
                desktopMax = uiMax,
                agentMin,
                agentMax
            });
        }
        else
        {
            _logger.Warn("MainWindowVM", "db.schema.incompatible", "Database schema incompatible", null, new
            {
                target,
                desktopMin = uiMin,
                desktopMax = uiMax,
                agentMin,
                agentMax,
                schemaOk = snapshot.SchemaOk,
                schemaValue = snapshot.CurrentVersion,
                schemaReason = snapshot.Reason,
                compatibility = snapshot.Compatibility.ToString()
            });
        }

        return new DbSchemaStartupState(
            Compatible: snapshot.Satisfied,
            ShouldMigrate: snapshot.ManualMigrationPolicy.ShouldExecuteMigration,
            Message: snapshot.IncompatibleMessage ?? "数据库版本不兼容",
            Target: snapshot.TargetVersion,
            DesktopMin: uiMin,
            DesktopMax: uiMax,
            AgentMin: agentMin,
            AgentMax: agentMax,
            SchemaOk: snapshot.SchemaOk,
            DbVersion: snapshot.CurrentVersion,
            Compatibility: snapshot.Compatibility.ToString(),
            Reason: snapshot.Reason);
    }

    private sealed record DbSchemaStartupState(
        bool Compatible,
        bool ShouldMigrate,
        string Message,
        string Target,
        string DesktopMin,
        string DesktopMax,
        string AgentMin,
        string AgentMax,
        bool SchemaOk,
        string? DbVersion,
        string Compatibility,
        string? Reason);

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updateSettings.Current.AutoCheckOnStartup)
        {
            return;
        }

        _logger.Info("MainWindowVM", "update.check.startup", "Auto checking updates on startup");
        await CheckAndPromptUpdateAsync(showNoUpdateToast: false, startupMode: true).ConfigureAwait(false);
    }

    private async Task CheckAndPromptUpdateAsync(bool showNoUpdateToast, bool startupMode)
    {
        if (_updates.IsChecking || IsUpdateApplying)
        {
            return;
        }

        await _updateDesktopFlow.CheckAndHandleAsync(
            showNoUpdateToast: showNoUpdateToast,
            startupMode: startupMode,
            applyNowAction: ApplyUpdateFlowAsync,
            ignoreVersionAction: IgnoreCurrentUpdateAsync,
            logScope: "MainWindowVM").ConfigureAwait(false);
    }

    private async Task ApplyUpdateFlowAsync()
    {
        if (IsUpdateApplying)
        {
            return;
        }

        IsUpdateApplying = true;
        try
        {
            await _updateDesktopFlow.ApplyUpdateFlowAsync().ConfigureAwait(false);
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
        => _updateDesktopFlow.IgnoreVersionAsync(LatestProductVersion);

    private void ShowDbConnectionFailed(string reason)
    {
        MarkDbConnectivityKnown();
        _lookup.InvalidateDrugCatalog();
        _dbEverDisconnected = true;

        PostOnUi(RaiseDbStateChanged);

        var now = DateTimeOffset.Now;

        if (now - _lastDbErrorToastAt < TimeSpan.FromSeconds(60)
            && string.Equals(_lastDbFailReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _lastDbErrorToastAt = now;
        _lastDbFailReason = reason;

        PostOnUi(() =>
        {
            _toasts.Error("数据连接失败", $"{reason}，请前往 [设置] 重新配置并测试连接");

        });
    }

    private void ShowDbDisconnected()
    {
        MarkDbConnectivityKnown();
        _lookup.InvalidateDrugCatalog();
        _dbEverDisconnected = true;
        _lastDbFailReason = null;

        PostOnUi(RaiseDbStateChanged);

        ScheduleAutoRefresh();
    }

    private void ShowDbReconnectedInfo()
    {
        MarkDbConnectivityKnown();
        PostOnUi(RaiseDbStateChanged);

        if (!_dbEverDisconnected)
        {
            return;
        }

        var now = DateTimeOffset.Now;

        if (now - _lastDbOkToastAt < TimeSpan.FromSeconds(15))
        {
            return;
        }

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
        {
            return;
        }

        try
        {
            var state = await GetDbSchemaStartupStateAsync(DatabaseMigrationTrigger.Reconnect).ConfigureAwait(false);
            if (!state.ShouldMigrate)
            {
                await RefreshSettingsSchemaStatusAsync("db_reconnected").ConfigureAwait(false);
                return;
            }

            using var cts = new CancellationTokenSource(StartupDbMigrationTimeout);
            var (migrationOk, summary) = await _settings
                .EnsureSchemaUpToDateAsync(
                    BuildSchemaContext(),
                    DatabaseMigrationTrigger.Reconnect,
                    ct: cts.Token)
                .ConfigureAwait(false);
            if (!migrationOk)
            {
                _logger.Warn("MainWindowVM", "db.reconnect.migrate.blocked",
                    "Reconnect migration blocked by policy", null, new { summary });
            }
            else if (TryParseAppliedCount(summary, out var applied) && applied > 0)
            {
                PostOnUi(() =>
                {
                    _toasts.Success("数据库结构更新", $"连接恢复后已自动应用 {applied} 个迁移");
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
        {
            return;
        }

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
        finally
        {
            PostOnUi(RaiseShellConnectivityChanged);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ManageSchemaRecoveryPolling(shouldPoll: false);

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
        WireDashboardFilterBar(null);

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
