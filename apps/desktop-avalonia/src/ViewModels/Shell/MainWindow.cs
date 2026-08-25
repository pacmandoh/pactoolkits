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
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Navigation;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Notifications;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Platform;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Security;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Update;
using PacToolkits.Desktop.Avalonia.Services.Workspace.Refresh;
using PacToolkits.Desktop.Avalonia.Ui.Threading;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 主窗口：导航、页面生命周期、Shell 连接、配置重载、更新轮询
/// 业务门禁与横幅只跟 PacAPI 可用性
/// </summary>
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private sealed record NavigationLocation(AppPageBase Page, int? TabIndex);
    private static readonly PageTab[] DashboardTabItems =
    [
        new("总览", "ChartLine"),
        new("录入", "ScanBarcode"),
        new("事务", "ArrowRightLeft"),
        new("异常", "OctagonAlert")
    ];
    private static readonly PageTab[] ScanCodeTabItems =
    [
        new("手动录入", "Keyboard"),
        new("自动拉取", "Cloud")
    ];
    private static readonly PageTab[] MsfxTabItems =
    [
        new("运行中心", "Gauge"),
        new("处理队列", "ListTodo"),
        new("上游核查", "ClipboardSearch")
    ];

    public ToastManager ToastManager { get; }
    public DialogManager DialogManager { get; }

    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IAppConfigStore _appConfigStore;
    private readonly IApiAvailabilityService _apiAvailability;
    private readonly ILookupCatalogService _lookup;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly IAgentsManager _agentsManager;
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IUpdateFlowService _updateFlow;
    private readonly IClientAliasService _clientAlias;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IUiBehaviorService _uiBehavior;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly ISensitiveUnlockService _unlockService;
    private readonly IAppLogger _logger;
    private readonly PageNavigationService _nav;
    private readonly string _configPath;
    private readonly string _configDir;
    private readonly string _configFile;
    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _configWatchCts;
    private string? _lastSeenConfigJson;

    private readonly TimeSpan _autoRefreshDebounce = TimeSpan.FromMilliseconds(180);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _updatePollCts;
    private CancellationTokenSource? _pageLifecycleCts;
    private int _pageLifecycleGeneration;
    private readonly WorkspaceDirtyRefresh _dirtyRefresh;

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
    private readonly PageHistory<NavigationLocation> _pageHistory = new();
    private NavigationLocation? _currentLocation;
    private AppPageBase? _activeLifecyclePage;
    private bool _isHistoryNavigation;
    private bool _disposed;

    private System.Windows.Input.ICommand? _lastRefreshCommand;
    private System.Windows.Input.ICommand? _lastImportCommand;
    private System.Windows.Input.ICommand? _lastExportCommand;
    private INotifyPropertyChanged? _topBarPageNotifier;


    [RelayCommand]
    private void OpenSettings()
    {
        if (SkipTrigger("main.nav.settings", 250))
        {
            return;
        }

        var page = _settingsPage;

        if (page is not null)
        {
            ObserveDetached(SetActivePageAsync(page), "page.active.detached.fail");
        }
    }

    private void NavigateSettingsTab(string debounceKey, Action<Settings> openTab)
    {
        if (SkipTrigger(debounceKey, 250))
        {
            return;
        }

        if (_settingsPage is not Settings settings)
        {
            return;
        }

        ObserveDetached(
            OpenSettingsTabAsync(settings, () => openTab(settings)),
            "page.active.detached.fail");
    }

    private async Task OpenSettingsTabAsync(Settings settings, Action openTab)
    {
        if (!ReferenceEquals(ActivePage, settings))
        {
            await SetActivePageAsync(settings).ConfigureAwait(true);
        }

        await RunOnUiAsync(openTab);
    }

    [RelayCommand]
    private async Task ShowAppInfo()
    {
        if (SkipTrigger("main.dialog.app_info", 250))
        {
            return;
        }

        var version = _releaseVersion.Current;
        await _dialogs.ShowAppInfo(new AppInfoArgs(
            version.ProductVersion,
            version.DesktopVersion,
            version.AgentsVersion,
            version.MinApiContract,
            version.MaxApiContract,
            version.BuildDate));
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

        ObserveDetached(SetActivePageAsync(page), "page.active.detached.fail");
    }

    [ObservableProperty] private AppPageBase? _activePage;
    [ObservableProperty] private ThemeMode _currentTheme = ThemeMode.Dark;
    [ObservableProperty] private bool _isSidebarExpanded = false;

    public string SidebarToggleIconKind => IsSidebarExpanded ? "PanelLeftClose" : "PanelLeftOpen";

    public string SidebarToggleToolTip => IsSidebarExpanded ? "折叠侧边栏" : "展开侧边栏";

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(SidebarToggleIconKind));
        OnPropertyChanged(nameof(SidebarToggleToolTip));
    }
    [ObservableProperty] private string? _activePageRoute;
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private string _currentProductVersion = "unknown";
    [ObservableProperty] private string _latestProductVersion = "unknown";

    public string ActivePageText => ActivePage?.DisplayName ?? "就绪";

    public string ActivePageIcon => ActivePage?.Icon ?? "PanelTop";

    public bool ShowActiveTab => ResolveTab(ActivePage) is not null;

    public string ActiveTabText => ResolveTab(ActivePage)?.Text ?? string.Empty;

    public string ActiveTabIcon => ResolveTab(ActivePage)?.Icon ?? "PanelTop";

    public string NavigateBackToolTip
        => _pageHistory.BackTarget is { } location ? $"后退到 {DescribeLocation(location)}" : "没有可后退位置";

    public string NavigateForwardToolTip
        => _pageHistory.ForwardTarget is { } location ? $"前进到 {DescribeLocation(location)}" : "没有可前进位置";

    public string VersionText
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

    public string VersionBarText
        => IsUpdateChecking ? "检查更新…" : VersionText;

    public bool IsUpdateApplying => _updateFlow.IsApplying;

    public string UpdateActionText => GetUpdateText(IsUpdateApplying, _updateFlow.ApplyProgress);

    public bool ShowConnectivityBanner { get; private set; }
    public string ConnectivityBannerTitle { get; private set; } = string.Empty;
    public string ConnectivityBannerMessage { get; private set; } = string.Empty;
    public bool ShowConnectivityBannerAction { get; private set; }
    public bool ConnectivityBannerIsError { get; private set; }
    public bool ConnectivityBannerIsWarning { get; private set; }
    public bool ConnectivityBannerIsInfo { get; private set; }

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
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(VersionBarText));
    }

    partial void OnIsUpdateCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(VersionBarText));
    }

    private void RaiseStatusItemsChanged()
    {
        RaiseApiChromeChanged();
        RaiseTopBarCanExecuteBindings();
        OnPropertyChanged(nameof(IsSettingsPageActive));
        OnPropertyChanged(nameof(ActivePageText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(VersionBarText));
    }

    private ConnectionKind _lastConnection = ConnectionKind.Unknown;
    private bool _sessionHadServiceDown;
    private DateTimeOffset _lastServiceOkToastAt = DateTimeOffset.MinValue;

    private void RaiseConnectivityChanged(ApiAvailabilitySnapshot api, bool configured)
    {
        var view = ConnectionView.From(api, configured);
        var banner = ConnectivityBanner.Create(api, isConfigured: configured);

        if (view.Kind != ConnectionKind.Up)
        {
            _unlockService.Lock(UnlockScopes.SharedOps);
        }

        if (view.Kind == ConnectionKind.Blocked)
        {
            _lookup.InvalidateDrugCatalog();
        }

        ShowConnectivityBanner = banner.IsVisible;
        ConnectivityBannerTitle = banner.Title;
        ConnectivityBannerMessage = banner.Message;
        ShowConnectivityBannerAction = banner.ShowOpenSettings;
        ConnectivityBannerIsError = banner.Severity == ConnectivitySeverity.Error;
        ConnectivityBannerIsWarning = banner.Severity == ConnectivitySeverity.Warning;
        ConnectivityBannerIsInfo = banner.Severity == ConnectivitySeverity.Info;

        OnPropertyChanged(nameof(ShowConnectivityBanner));
        OnPropertyChanged(nameof(ConnectivityBannerTitle));
        OnPropertyChanged(nameof(ConnectivityBannerMessage));
        OnPropertyChanged(nameof(ShowConnectivityBannerAction));
        OnPropertyChanged(nameof(ConnectivityBannerIsError));
        OnPropertyChanged(nameof(ConnectivityBannerIsWarning));
        OnPropertyChanged(nameof(ConnectivityBannerIsInfo));

        SyncServicePagesAvailability(api);

        RaiseStatusItemsChanged();
    }

    private void OnApiAvailabilityChanged()
    {
        var api = _apiAvailability.Current;
        var configured = _apiAvailability.IsConfigured;
        PostOnUi(() =>
        {
            var view = ConnectionView.From(api, configured);
            var becameUp = view.Kind == ConnectionKind.Up && _lastConnection != ConnectionKind.Up;
            if (view.Kind is ConnectionKind.Down or ConnectionKind.Blocked)
            {
                _sessionHadServiceDown = true;
            }

            _lastConnection = view.Kind;

            RaiseConnectivityChanged(api, configured);
            if (becameUp)
            {
                ScheduleAutoRefresh();
                TryShowServiceReconnectedToast();
            }
        });
    }

    /// <summary>曾断开后再恢复才 Info toast，并做短防抖</summary>
    private void TryShowServiceReconnectedToast()
    {
        if (!_sessionHadServiceDown)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        if (now - _lastServiceOkToastAt < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _sessionHadServiceDown = false;
        _lastServiceOkToastAt = now;
        _toasts.Info("PacAPI 服务已恢复", "连接已恢复");
    }

    private void SyncServicePagesAvailability(ApiAvailabilitySnapshot api)
    {
        foreach (var page in WorkspacePages)
        {
            page.SyncConnection(api);
        }
    }

    private void LogPageInfo(string eventName, string message, object? context = null)
        => _logger.Info("MainWindowVM", eventName, message, context, LogTrace.Current);

    private static void ObserveDetached(Task task, string eventName, string? message = null)
        => TaskObserve.Observe(task, "MainWindowVM", eventName, message ?? "Detached task failed");

    private static Task RunOnUiAsync(Action action)
        => UiThreadHelper.RunOnUiAsync(action, DispatcherPriority.Background);

    private static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
        => await UiThreadHelper.RunOnUiAsync(action, priority);

    private static void PostOnUi(Action action)
        => UiThreadHelper.PostOnUi(action, DispatcherPriority.Background);

    private static void PostOnUi(Action action, DispatcherPriority priority)
        => UiThreadHelper.PostOnUi(action, priority);

    private ITopBarActions? ActiveTopBar => ActivePage;
    private Dashboard? _dashboard;
    private ScanCode? _scanCode;
    private MsfxLink? _msfx;

    public System.Windows.Input.ICommand? TopRefresh => ActiveTopBar?.RefreshCommand;
    public System.Windows.Input.ICommand? TopImport => ActiveTopBar?.ImportCommand;
    public System.Windows.Input.ICommand? TopExport => ActiveTopBar?.ExportCommand;

    public bool ShowTopRefresh => TopRefresh is not null;
    public bool ShowTopImport => TopImport is not null;
    public bool ShowTopExport => TopExport is not null;

    public bool CanTopRefresh => CanWorkspaceRefresh() && TopRefresh?.CanExecute(null) == true;
    public bool CanTopImport => TopImport?.CanExecute(null) == true;
    public bool CanTopExport => TopExport?.CanExecute(null) == true;

    [RelayCommand]
    private void RefreshActivePage()
    {
        if (SkipTrigger("main.top.refresh", 300))
        {
            return;
        }

        if (!CanWorkspaceRefresh())
        {
            return;
        }

        var cmd = TopRefresh;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        if (SkipTrigger("main.top.update.check", 450))
        {
            return;
        }

        await PromptUpdateAsync(silent: false).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenUpdateCenterAsync()
    {
        if (SkipTrigger("main.top.update.open", 450))
        {
            return;
        }

        if (IsUpdateApplying)
        {
            _toasts.Info("应用更新", "更新正在处理中，请稍候");
            return;
        }

        await _updateFlow.ApplyUpdateFlowAsync().ConfigureAwait(false);
    }

    public MainWindowViewModel(
        IEnumerable<AppPageBase> pages,
        PageNavigationService nav,
        IToastService toasts,
        IDialogService dialogs,
        IAppConfigStore appConfigStore,
        ToastManager toastManager,
        DialogManager dialogManager,
        IApiAvailabilityService apiAvailability,
        ILookupCatalogService lookup,
        IChangeWatermarkService changeWatermark,
        IAgentsManager agentsManager,
        IReleaseVersionService releaseVersion,
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IUpdateFlowService updateFlow,
        IClientAliasService clientAlias,
        ILoggingSettingsService loggingSettings,
        IUiBehaviorService uiBehavior,
        ITraceCodeRuleService traceCodeRule,
        ISensitiveUnlockService unlockService,
        IAppLogger logger,
        WorkspaceDirtyRefresh dirtyRefresh)
    {
        _toasts = toasts;
        _dialogs = dialogs;
        _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
        _apiAvailability = apiAvailability ?? throw new ArgumentNullException(nameof(apiAvailability));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _agentsManager = agentsManager ?? throw new ArgumentNullException(nameof(agentsManager));
        _releaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
        _observedUpdateOptions = _updateSettings.Current;
        _updateFlow = updateFlow ?? throw new ArgumentNullException(nameof(updateFlow));
        _clientAlias = clientAlias ?? throw new ArgumentNullException(nameof(clientAlias));
        _loggingSettings = loggingSettings ?? throw new ArgumentNullException(nameof(loggingSettings));
        _uiBehavior = uiBehavior ?? throw new ArgumentNullException(nameof(uiBehavior));
        _traceCodeRule = traceCodeRule ?? throw new ArgumentNullException(nameof(traceCodeRule));
        _unlockService = unlockService ?? throw new ArgumentNullException(nameof(unlockService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dirtyRefresh = dirtyRefresh ?? throw new ArgumentNullException(nameof(dirtyRefresh));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));

        _dirtyRefresh.Configure(
            CanWorkspaceRefresh,
            work => PostOnUi(() => ObserveDetached(work(), "workspace.dirty_refresh.fail")),
            (page, ex) => _logger.Warn(
                "MainWindowVM",
                "page.refresh.active_fail",
                "Active page refresh failed",
                ex,
                new { page = page.GetType().Name },
                LogTrace.Current));


        ToastManager = toastManager;
        DialogManager = dialogManager;

        _themeWatcher = new ThemeWatcher(global::Avalonia.Application.Current!);
        _themeWatcher.Initialize();

        _configPath = _appConfigStore.ConfigPath;
        _configDir = Path.GetDirectoryName(_configPath) ?? string.Empty;
        _configFile = Path.GetFileName(_configPath);

        var ordered = pages.OrderBy(p => p.Index).ToList();

        WorkspacePages = new AvaloniaList<AppPageBase>(ordered);
        SidebarPages = ordered.Where(p => p.ShowInSidebar).ToList();
        _pageByType = ordered.ToDictionary(p => p.GetType(), p => p);
        _settingsPage = ordered.FirstOrDefault(p => p is ISettingsPage);

        _nav.NavigationRequested += OnNavigationRequested;

        ActivePage = SidebarPages.FirstOrDefault() ?? ordered.FirstOrDefault();
        ActivePageRoute = ActivePage?.SidebarRoute;
        SelectedFunctionArea = ShellFunctionAreas.Resolve(ActivePage?.FunctionAreaId);
        RebuildFilteredSidebarPages();
        CurrentTheme = ResolveThemeMode(global::Avalonia.Application.Current?.RequestedThemeVariant);

        _changeWatermark.TopicChanged += OnTopicChanged;
        _apiAvailability.Changed += OnApiAvailabilityChanged;

        _updates.Changed += OnUpdateChanged;
        _updateFlow.StateChanged += OnUpdateFlowStateChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;

        CurrentProductVersion = _updates.CurrentVersion;
        LatestProductVersion = _updates.LatestVersion;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;

        AttachChromeHooks();

        ObserveDetached(CheckConfigOnStartupAsync(), "startup.config.detached.fail");
        StartConfigWatcher();
        _lastConnection = ConnectionView.From(_apiAvailability.Current, _apiAvailability.IsConfigured).Kind;
        RaiseConnectivityChanged(_apiAvailability.Current, _apiAvailability.IsConfigured);
        ObserveDetached(InitializeAfterStartupChecksAsync(), "startup.init.detached.fail");
        _logger.Info("MainWindowVM", "main.init", "Main window initialized");
    }

    private async Task InitializeAfterStartupChecksAsync()
    {
        try
        {
            // 周期探测自己做首检；未配置时不发 HTTP
            _apiAvailability.Start();

            MarkDirtyByType<MsfxLink>();

            if (_apiAvailability.IsConfigured)
            {
                _changeWatermark.Start();
            }

            await StartAgentsOnStartupAsync().ConfigureAwait(false);
            await AlignUpdateChannelOnStartupAsync().ConfigureAwait(false);
            await CheckUpdatesOnStartupAsync().ConfigureAwait(false);
            RestartUpdatePolling();
        }
        finally
        {
            ScheduleAutoRefresh();
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

    private void WireDashboard(AppPageBase? page)
    {
        _dashboard?.PropertyChanged -= OnDashboardPropertyChanged;

        _dashboard = page as Dashboard;

        _dashboard?.PropertyChanged += OnDashboardPropertyChanged;
    }

    private void WireScanCode(AppPageBase? page)
    {
        _scanCode?.PropertyChanged -= OnScanCodePropertyChanged;

        _scanCode = page as ScanCode;

        _scanCode?.PropertyChanged += OnScanCodePropertyChanged;
    }

    private void WireMsfx(AppPageBase? page)
    {
        _msfx?.PropertyChanged -= OnMsfxPropertyChanged;

        _msfx = page as MsfxLink;

        _msfx?.PropertyChanged += OnMsfxPropertyChanged;
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Dashboard.SelectedTabIndex))
        {
            if (ReferenceEquals(ActivePage, _dashboard))
            {
                TrackLocation(_dashboard);
            }

            RaiseBreadcrumbBindings();
        }
    }

    private void OnScanCodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanCode.SelectedTabIndex))
        {
            if (ReferenceEquals(ActivePage, _scanCode))
            {
                TrackLocation(_scanCode);
            }

            RaiseBreadcrumbBindings();
        }
    }

    private void OnMsfxPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MsfxLink.SelectedTabIndex))
        {
            if (ReferenceEquals(ActivePage, _msfx))
            {
                TrackLocation(_msfx);
            }

            RaiseBreadcrumbBindings();
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
        OnPropertyChanged(nameof(TopRefresh));
        OnPropertyChanged(nameof(TopImport));
        OnPropertyChanged(nameof(TopExport));
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
            ObserveDetached(SetActivePageAsync(firstInArea), "page.active.detached.fail");
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
        TrackLocation(value);

        if (!ReferenceEquals(previous, value))
        {
            _activeLifecyclePage = value;
            _pageLifecycleCts?.Cancel();
            _pageLifecycleCts?.Dispose();
            _pageLifecycleCts = new CancellationTokenSource();
            // 被更快侧栏切换顶替的生命周期不得在错误页面上收尾
            var generation = ++_pageLifecycleGeneration;
            ObserveDetached(
                RunPageLifecycleTransitionAsync(previous, value, generation, _pageLifecycleCts.Token),
                "page.lifecycle.detached.fail");
        }

        value?.SyncPageAvailability();

        if (value is not null
            && value.ShowInSidebar
            && !string.Equals(SelectedFunctionArea.Id, value.FunctionAreaId, StringComparison.Ordinal))
        {
            SelectedFunctionArea = ShellFunctionAreas.Resolve(value.FunctionAreaId);
        }

        WireTopBarCommands(value);
        WireDashboard(value);
        WireScanCode(value);
        WireMsfx(value);

        RaiseBreadcrumbBindings();
        RaiseTopBarVisibilityBindings();
        RaiseStatusItemsChanged();
        RaiseNavigationHistoryChanged();

        TryRefreshDirtyActivePage(silent: false);
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
            ObserveDetached(SetActivePageAsync(page), "page.active.detached.fail");
        }
    }

    private async Task RunPageLifecycleTransitionAsync(
        AppPageBase? previous,
        AppPageBase? current,
        int generation,
        CancellationToken ct)
    {
        using var traceScope = LogTrace.Begin();
        var fromPage = previous?.GetType().Name;
        var toPage = current?.GetType().Name;

        try
        {
            if (previous is IPageLifecycleAware oldPage)
            {
                LogPageInfo("page.deactivated", "Page deactivated", new { page = fromPage, next = toPage });
                await oldPage.OnPageDeactivatedAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogPageInfo("page.lifecycle.skipped", "Page lifecycle superseded", new
            {
                reason = "deactivate_cancelled",
                page = fromPage,
                next = toPage
            });
            return;
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.lifecycle.deactivate_fail", "Page deactivation failed", ex, new
            {
                page = fromPage
            }, LogTrace.Current);
        }

        if (ct.IsCancellationRequested || generation != _pageLifecycleGeneration)
        {
            LogPageInfo("page.lifecycle.skipped", "Page lifecycle superseded", new
            {
                reason = "generation_changed",
                page = fromPage,
                next = toPage
            });
            return;
        }

        try
        {
            if (current is IPageLifecycleAware newPage)
            {
                await newPage.OnPageActivatedAsync(ct).ConfigureAwait(false);
                LogPageInfo("page.activated", "Page activated", new { page = toPage, previous = fromPage });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogPageInfo("page.lifecycle.skipped", "Page lifecycle superseded", new
            {
                reason = "activate_cancelled",
                page = toPage
            });
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.lifecycle.activate_fail", "Page activation failed", ex, new
            {
                page = toPage
            }, LogTrace.Current);
        }

        if (generation != _pageLifecycleGeneration || current is null)
        {
            return;
        }

        await RunOnUiAsync(() => current.SyncPageAvailability(), DispatcherPriority.Loaded);
    }

    private void OnNavigationRequested(Type pageType)
    {
        if (_pageByType.TryGetValue(pageType, out var page))
        {
            ObserveDetached(SetActivePageAsync(page), "page.active.detached.fail");
        }
    }

    private bool CanNavigateBack() => _pageHistory.CanGoBack;

    private bool CanNavigateForward() => _pageHistory.CanGoForward;

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private Task NavigateBackAsync() => NavigateHistoryAsync(goBack: true);

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private Task NavigateForwardAsync() => NavigateHistoryAsync(goBack: false);

    private static NavigationLocation? GetLocation(AppPageBase? page)
        => page switch
        {
            Dashboard dashboard => new NavigationLocation(dashboard, dashboard.SelectedTabIndex),
            ScanCode scanCode => new NavigationLocation(scanCode, scanCode.SelectedTabIndex),
            MsfxLink msfx => new NavigationLocation(msfx, msfx.SelectedTabIndex),
            not null => new NavigationLocation(page, null),
            _ => null
        };

    private static PageTab? ResolveTab(AppPageBase? page, int? tabIndex = null)
    {
        var index = tabIndex ?? page switch
        {
            Dashboard dashboard => dashboard.SelectedTabIndex,
            ScanCode scanCode => scanCode.SelectedTabIndex,
            MsfxLink msfx => msfx.SelectedTabIndex,
            _ => -1
        };

        var tabs = page switch
        {
            Dashboard => DashboardTabItems,
            ScanCode => ScanCodeTabItems,
            MsfxLink => MsfxTabItems,
            _ => null
        };

        return tabs is not null && index >= 0 && index < tabs.Length
            ? tabs[index]
            : null;
    }

    private static string DescribeLocation(NavigationLocation location)
        => ResolveTab(location.Page, location.TabIndex) is { } tab
            ? $"{location.Page.DisplayName} / {tab.Text}"
            : location.Page.DisplayName;

    private void TrackLocation(AppPageBase? page)
    {
        var next = GetLocation(page);
        if (next is null || EqualityComparer<NavigationLocation>.Default.Equals(_currentLocation, next))
        {
            return;
        }

        if (!_isHistoryNavigation)
        {
            _pageHistory.Record(_currentLocation, next);
        }

        _currentLocation = next;
        RaiseBreadcrumbBindings();
        RaiseNavigationHistoryChanged();
    }

    private static void ApplyLocation(NavigationLocation location)
    {
        if (location.TabIndex is not { } tabIndex)
        {
            return;
        }

        switch (location.Page)
        {
            case Dashboard dashboard:
                dashboard.SelectedTabIndex = tabIndex;
                break;
            case ScanCode scanCode:
                scanCode.SelectedTabIndex = tabIndex;
                break;
            case MsfxLink msfx:
                msfx.SelectedTabIndex = tabIndex;
                break;
        }
    }

    private async Task NavigateHistoryAsync(bool goBack)
    {
        var target = goBack ? _pageHistory.BackTarget : _pageHistory.ForwardTarget;
        var current = _currentLocation ?? GetLocation(ActivePage);
        if (target is null || current is null)
        {
            return;
        }

        _isHistoryNavigation = true;
        try
        {
            await SetActivePageAsync(target.Page);
            if (!ReferenceEquals(ActivePage, target.Page))
            {
                return;
            }

            ApplyLocation(target);
            var reached = GetLocation(ActivePage);
            if (!EqualityComparer<NavigationLocation>.Default.Equals(reached, target))
            {
                return;
            }

            _currentLocation = reached;

            if (goBack)
            {
                _pageHistory.CompleteBack(current);
            }
            else
            {
                _pageHistory.CompleteForward(current);
            }
        }
        finally
        {
            _isHistoryNavigation = false;
            RaiseNavigationHistoryChanged();
        }
    }

    private void RaiseNavigationHistoryChanged()
    {
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(NavigateBackToolTip));
        OnPropertyChanged(nameof(NavigateForwardToolTip));
    }

    private void RaiseBreadcrumbBindings()
    {
        OnPropertyChanged(nameof(ActivePageText));
        OnPropertyChanged(nameof(ActivePageIcon));
        OnPropertyChanged(nameof(ShowActiveTab));
        OnPropertyChanged(nameof(ActiveTabText));
        OnPropertyChanged(nameof(ActiveTabIcon));
    }

    private async Task SetActivePageAsync(AppPageBase? page)
    {
        using var traceScope = LogTrace.Begin();
        var fromPage = ActivePage?.GetType().Name;
        var toPage = page?.GetType().Name;
        LogPageInfo("page.navigate.started", "Page navigation started", new { from = fromPage, to = toPage });

        if (page is null || !page.IsEnabled)
        {
            LogPageInfo("page.navigate.skipped", "Page navigation skipped", new
            {
                reason = "null_or_disabled",
                from = fromPage,
                to = toPage
            });
            return;
        }

        if (ReferenceEquals(ActivePage, page))
        {
            LogPageInfo("page.navigate.skipped", "Page navigation skipped", new
            {
                reason = "already_active",
                page = toPage
            });
            return;
        }

        if (ActivePage is ISettingsPage settings && page is not Settings)
        {
            var ok = await settings.TrySaveOrDiscardAllAsync();
            if (!ok)
            {
                LogPageInfo("page.navigate.skipped", "Page navigation skipped", new
                {
                    reason = "settings_discard_cancelled",
                    from = fromPage,
                    to = toPage
                });
                return;
            }
        }

        ActivePage = page;
        LogPageInfo("page.navigate.finished", "Page navigation finished", new
        {
            from = fromPage,
            to = toPage,
            route = page.SidebarRoute
        });
    }

    private async Task AlignUpdateChannelOnStartupAsync()
    {
        await _updates.AlignChannelAsync(
            async (configured, installed, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return await _dialogs.Confirm(
                    "更新通道与安装包不一致",
                    $"当前配置为 {configured} 通道，但安装包为 {installed}\n\n" +
                    $"是否将更新通道同步为 {installed}？\n" +
                    "选择「取消」将保留当前配置，并记住本次安装包通道，避免反复询问");
            }).ConfigureAwait(false);
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updateSettings.Current.AutoCheckOnStartup)
        {
            return;
        }

        _logger.Info("MainWindowVM", "update.check.startup", "Auto checking updates on startup");
        await PromptUpdateAsync(silent: true).ConfigureAwait(false);
    }

    private async Task PromptUpdateAsync(bool silent)
    {
        if (_updates.IsChecking || IsUpdateApplying)
        {
            return;
        }

        await _updateFlow.CheckAndHandleAsync(
            silent: silent,
            logScope: "MainWindowVM").ConfigureAwait(false);
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

    private void OnUpdateFlowStateChanged()
    {
        PostOnUi(() =>
        {
            OnPropertyChanged(nameof(IsUpdateApplying));
            OnPropertyChanged(nameof(UpdateActionText));
        });
    }

    internal static string GetUpdateText(bool isApplying, int progress)
        => isApplying ? $"更新 · {Math.Clamp(progress, 0, 100)}%" : "更新";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        SafeExecute(() => _nav.NavigationRequested -= OnNavigationRequested);
        SafeExecute(() => _apiAvailability.Changed -= OnApiAvailabilityChanged);
        SafeExecute(() => _changeWatermark.TopicChanged -= OnTopicChanged);
        SafeExecute(DetachChromeHooks);
        SafeExecute(() => _updates.Changed -= OnUpdateChanged);
        SafeExecute(() => _updateFlow.StateChanged -= OnUpdateFlowStateChanged);
        SafeExecute(() => _updateSettings.Changed -= OnUpdateSettingsChanged);

        WireTopBarCommands(null);
        WireDashboard(null);
        WireScanCode(null);
        WireMsfx(null);

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
            var configWatchCts = Interlocked.Exchange(ref _configWatchCts, null);
            configWatchCts?.Cancel();
        });

        SafeExecute(() =>
        {
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
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
