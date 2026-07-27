using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Core;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Workspace;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 协调主窗口导航、页面生命周期、数据库连接反馈、配置重载和更新轮询
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
    private readonly IDbConfigNotifier _dbConfigNotifier;
    private readonly IDbConnectionTester _dbConnectionTester;
    private readonly IDbConnectionMonitorService _dbMonitor;
    private readonly IDbAccessGuard _accessGuard;
    private readonly ILookupCatalogService _lookup;
    private readonly ISettingsService _settings;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly IAgentsManager _agentsManager;
    private IAgentsRuntime Agents => _agentsManager.GetRequired(AgentsIds.Agents);
    private readonly IReleaseVersionService _releaseVersion;
    private readonly IAppStartupStateService _startupState;
    private readonly IAppUpdateService _updates;
    private readonly IUpdateSettingsService _updateSettings;
    private readonly IUpdateFlowService _updateFlow;
    private readonly IClientAliasService _clientAlias;
    private readonly ILoggingSettingsService _loggingSettings;
    private readonly IUiBehaviorService _uiBehavior;
    private readonly ITraceCodeRuleService _traceCodeRule;
    private readonly IAppLogger _logger;
    private readonly PageNavigationService _nav;
    private readonly string _configPath;
    private readonly string _configDir;
    private readonly string _configFile;
    private FileSystemWatcher? _configWatcher;
    private volatile bool _isApplyingConfig;
    private DateTimeOffset _lastDbErrorToastAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDbOkToastAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAgentsTopToastAt = DateTimeOffset.MinValue;
    // 顶栏操作节流与 Host 命令冷却保持一致，避免同一请求产生重复反馈
    private static readonly TimeSpan AgentsTopToastDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan TopActionDebounce = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private string? _lastDbFailReason;
    private string? _lastSeenConfigJson;
    private bool _dbEverDisconnected;
    private bool _isDbConnectivityKnown;
    private bool _wasAccessGuardBlocked;
    private CancellationTokenSource? _schemaRecoveryCts;

    private readonly TimeSpan _autoRefreshDebounce = TimeSpan.FromMilliseconds(180);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _dbBootstrapCts;
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
    private readonly Dictionary<string, ModuleChrome> _moduleChromeById = new(StringComparer.Ordinal);

    public ObservableCollection<ModuleChrome> TopStatusPills { get; } = new();

    public ObservableCollection<ModuleChrome> BottomStatusBar { get; } = new();
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

    [RelayCommand]
    private async Task ShowAppInfo()
    {
        if (SkipTrigger("main.dialog.app_info", 250))
        {
            return;
        }

        var version = _releaseVersion.Current;
        await _dialogs.ShowAppInfo(new AppInfoArgs(version.ProductVersion, version.BuildDate));
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
    [ObservableProperty] private bool _isDbProbeRunning;
    [ObservableProperty] private bool _isAgentsActionRunning;
    [ObservableProperty] private bool _isUpdateChecking;
    [ObservableProperty] private bool _hasUpdateAvailable;
    [ObservableProperty] private string _currentProductVersion = "unknown";
    [ObservableProperty] private string _latestProductVersion = "unknown";
    public bool CanProbeDb() => !IsDbProbeRunning;
    public bool CanControlAgents()
        => !IsAgentsActionRunning && Agents.HostState != AgentsRunState.Starting;

    public bool IsDbConnected => _dbMonitor.IsConnected;

    public string DbItemText
        => IsDbProbeRunning ? "数据库：检测中…"
        : IsDbConnected ? "数据库：已连接"
        : "数据库：未连接";

    public string HostItemText => $"Host：{HostStatusText}";

    public string ActivePageText => ActivePage?.DisplayName ?? "就绪";

    public string ActivePageIcon => ActivePage?.Icon ?? "PanelTop";

    public bool ShowActiveTab => ResolveTab(ActivePage) is not null;

    public string ActiveTabText => ResolveTab(ActivePage)?.Text ?? string.Empty;

    public string ActiveTabIcon => ResolveTab(ActivePage)?.Icon ?? "PanelTop";

    public string NavigateBackToolTip
        => _pageHistory.BackTarget is { } location ? $"后退到 {DescribeLocation(location)}" : "没有可后退位置";

    public string NavigateForwardToolTip
        => _pageHistory.ForwardTarget is { } location ? $"前进到 {DescribeLocation(location)}" : "没有可前进位置";

    public bool ShowAccessGuardItem => _accessGuard.IsBlocked;

    public string AccessGuardItemText
        => string.IsNullOrWhiteSpace(_accessGuard.BlockReason)
            ? "配置未完成"
            : _accessGuard.BlockReason!;

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

    public bool IsSettingsPageActive => ActivePage is ISettingsPage;

    public bool IsHostRunning => Agents.IsHostRunning;

    public bool IsHostStarting => Agents.HostState == AgentsRunState.Starting;

    public bool IsHostInactive => !Agents.HostState.IsActive();

    public string HostStatusText
        => Agents.HostState switch
        {
            AgentsRunState.Running => "运行中",
            AgentsRunState.Starting => "启动中",
            AgentsRunState.Failed => "启动失败",
            AgentsRunState.Stopped => "未启动",
            _ => "未知",
        };

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
        OnPropertyChanged(nameof(DbItemText));
        RaiseConnectivityChanged();
    }

    private void RaiseStatusItemsChanged()
    {
        OnPropertyChanged(nameof(DbItemText));
        OnPropertyChanged(nameof(HostItemText));
        OnPropertyChanged(nameof(ActivePageText));
        OnPropertyChanged(nameof(ShowAccessGuardItem));
        OnPropertyChanged(nameof(AccessGuardItemText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(VersionBarText));
    }

    private void RaiseConnectivityChanged()
    {
        var wasBlocked = _wasAccessGuardBlocked;
        var banner = ConnectivityBanner.Create(
            IsDbConnected,
            _isDbConnectivityKnown,
            _accessGuard);
        var isBlocked = _accessGuard.IsBlocked;
        _wasAccessGuardBlocked = isBlocked;

        if (isBlocked)
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
        SyncAllPagesAvailability();

        if (wasBlocked && !isBlocked)
        {
            ScheduleAutoRefresh();
        }

        ManageSchemaRecoveryPolling(isBlocked && IsDbConnected && !IsDbProbeRunning);
        RaiseStatusItemsChanged();
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
        ObserveDetached(RunSchemaRecoveryPollingAsync(_schemaRecoveryCts.Token), "schema.recovery.detached.fail");
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

                await RefreshSchemaStatusAsync("guard_recovery_poll").ConfigureAwait(false);
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
            page.SyncPageAvailability();
        }
    }

    partial void OnIsDbProbeRunningChanged(bool value)
    {
        TryReconnectDbCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DbItemText));
    }

    partial void OnIsAgentsActionRunningChanged(bool value)
    {
        StartOrRestartHostCommand.NotifyCanExecuteChanged();
        StartOrRestartModuleCommand.NotifyCanExecuteChanged();
    }

    private void RaiseAgentsStateChanged()
    {
        OnPropertyChanged(nameof(IsHostRunning));
        OnPropertyChanged(nameof(IsHostStarting));
        OnPropertyChanged(nameof(IsHostInactive));
        OnPropertyChanged(nameof(HostStatusText));
        OnPropertyChanged(nameof(HostItemText));
        SyncModuleChrome();
    }

    private void SyncModuleChrome()
    {
        var scanned = Agents.Modules;
        var wanted = scanned
            .Where(m => m.Desktop.TopStatusPills || m.Desktop.BottomStatusBar)
            .ToList();

        foreach (var staleId in _moduleChromeById.Keys.Except(wanted.Select(m => m.Id), StringComparer.Ordinal).ToList())
        {
            _moduleChromeById.Remove(staleId);
        }

        foreach (var module in wanted)
        {
            if (!_moduleChromeById.TryGetValue(module.Id, out var chrome))
            {
                chrome = new ModuleChrome(module);
                _moduleChromeById[module.Id] = chrome;
            }
            else
            {
                chrome.ApplyDescriptor(module);
            }

            chrome.Apply(Agents.GetModuleState(module.Id));
        }

        ReplaceModuleChrome(TopStatusPills, wanted.Where(m => m.Desktop.TopStatusPills));
        ReplaceModuleChrome(BottomStatusBar, wanted.Where(m => m.Desktop.BottomStatusBar));
    }

    private void ReplaceModuleChrome(
        ObservableCollection<ModuleChrome> target,
        IEnumerable<ModuleDescriptor> modules)
    {
        var next = modules
            .Select(m => _moduleChromeById[m.Id])
            .ToList();

        if (target.Count == next.Count &&
            target.Zip(next, (a, b) => ReferenceEquals(a, b)).All(same => same))
        {
            return;
        }

        for (var index = 0; index < next.Count; index++)
        {
            var item = next[index];
            if (index < target.Count && ReferenceEquals(target[index], item))
            {
                continue;
            }

            var currentIndex = target.IndexOf(item);
            if (currentIndex >= 0)
            {
                target.Move(currentIndex, index);
            }
            else
            {
                target.Insert(index, item);
            }
        }

        while (target.Count > next.Count)
        {
            target.RemoveAt(target.Count - 1);
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

    public bool CanTopRefresh => TopRefresh?.CanExecute(null) == true;
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
            _toasts.Info("刷新", "数据库检查进行中，请稍候");
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
        IDbConfigNotifier dbConfigNotifier,
        IDbConnectionTester dbConnectionTester,
        ToastManager toastManager,
        DialogManager dialogManager,
        IDbConnectionMonitorService dbMonitor,
        IDbAccessGuard accessGuard,
        ILookupCatalogService lookup,
        ISettingsService settings,
        IChangeWatermarkService changeWatermark,
        IAgentsManager agentsManager,
        IReleaseVersionService releaseVersion,
        IAppStartupStateService startupState,
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IUpdateFlowService updateFlow,
        IClientAliasService clientAlias,
        ILoggingSettingsService loggingSettings,
        IUiBehaviorService uiBehavior,
        ITraceCodeRuleService traceCodeRule,
        IAppLogger logger,
        WorkspaceDirtyRefresh dirtyRefresh)
    {
        _toasts = toasts;
        _dialogs = dialogs;
        _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
        _dbConfigNotifier = dbConfigNotifier ?? throw new ArgumentNullException(nameof(dbConfigNotifier));
        _dbConnectionTester = dbConnectionTester ?? throw new ArgumentNullException(nameof(dbConnectionTester));
        _dbMonitor = dbMonitor ?? throw new ArgumentNullException(nameof(dbMonitor));
        _accessGuard = accessGuard ?? throw new ArgumentNullException(nameof(accessGuard));
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _agentsManager = agentsManager ?? throw new ArgumentNullException(nameof(agentsManager));
        _releaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
        _startupState = startupState ?? throw new ArgumentNullException(nameof(startupState));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _updateSettings = updateSettings ?? throw new ArgumentNullException(nameof(updateSettings));
        _observedUpdateOptions = _updateSettings.Current;
        _updateFlow = updateFlow ?? throw new ArgumentNullException(nameof(updateFlow));
        _clientAlias = clientAlias ?? throw new ArgumentNullException(nameof(clientAlias));
        _loggingSettings = loggingSettings ?? throw new ArgumentNullException(nameof(loggingSettings));
        _uiBehavior = uiBehavior ?? throw new ArgumentNullException(nameof(uiBehavior));
        _traceCodeRule = traceCodeRule ?? throw new ArgumentNullException(nameof(traceCodeRule));
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


        _dbConfigNotifier.Applied += OnDbConfigAppliedEvent;

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

        _dbMonitor.ConnectionFailed += ShowDbConnectionFailed;
        _dbMonitor.Disconnected += ShowDbDisconnected;
        _dbMonitor.Reconnected += ShowDbReconnectedInfo;
        _dbMonitor.Reconnected += OnDbReconnectedRefreshSchema;
        _changeWatermark.TopicChanged += OnTopicChanged;

        _dbMonitor.Reconnected += ScheduleAutoRefresh;
        _dbMonitor.Disconnected += ScheduleAutoRefresh;
        Agents.StatusChanged += OnAgentsStatusChanged;
        _updates.Changed += OnUpdateChanged;
        _updateFlow.StateChanged += OnUpdateFlowStateChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;

        CurrentProductVersion = _updates.CurrentVersion;
        LatestProductVersion = _updates.LatestVersion;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        IsUpdateChecking = _updates.IsChecking;

        ObserveDetached(CheckConfigOnStartupAsync(), "startup.config.detached.fail");
        StartConfigWatcher();
        RaiseAgentsStateChanged();
        _wasAccessGuardBlocked = _accessGuard.IsBlocked;
        RaiseConnectivityChanged();
        ObserveDetached(InitializeAfterStartupChecksAsync(), "startup.init.detached.fail");
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

            await CheckDbOnStartupAsync().ConfigureAwait(false);
            await RefreshSchemaStatusAsync("startup_postcheck").ConfigureAwait(false);
            MarkDirtyByType<MsfxLink>();

            if (File.Exists(_configPath))
            {
                _changeWatermark.Start();
                StartDbStateBootstrap();
            }

            _startupState.MarkDbInitCompleted();

            await StartAgentsOnStartupAsync().ConfigureAwait(false);
            await AlignUpdateChannelOnStartupAsync().ConfigureAwait(false);
            await CheckUpdatesOnStartupAsync().ConfigureAwait(false);
            RestartUpdatePolling();
        }
        finally
        {
            _startupState.MarkDbInitCompleted();
        }
    }

    private async Task StartAgentsOnStartupAsync()
    {
        if (Agents.IsHostRunning)
        {
            return;
        }

        try
        {
            var result = await Agents.StartOrRestartAsync().ConfigureAwait(false);
            if (!result.Ok && !result.SuppressToast)
            {
                _logger.Warn(
                    "MainWindowVM",
                    "agents.startup_autostart.fail",
                    "Failed to auto-start Agents host on startup",
                    null,
                    new { result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "agents.startup_autostart.exception", "Startup auto-start threw exception", ex);
        }
        finally
        {
            PostOnUi(RaiseAgentsStateChanged);
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
        ObserveDetached(RunDbStateBootstrapAsync(_dbBootstrapCts.Token), "db.bootstrap.detached.fail");
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

        if (value is Settings settingsPage)
        {
            ObserveDetached(settingsPage.RefreshSchemaStatusAsync("open_settings"), "schema.refresh.detached.fail");
        }

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

        OnPropertyChanged(nameof(IsSettingsPageActive));
        RaiseBreadcrumbBindings();
        RaiseTopBarVisibilityBindings();
        RaiseStatusItemsChanged();
        RaiseNavigationHistoryChanged();

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

    [RelayCommand(CanExecute = nameof(CanProbeDb))]
    private async Task TryReconnectDb()
    {
        if (SkipTrigger("top.db.probe", (int)TopActionDebounce.TotalMilliseconds))
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

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StartOrRestartHost()
    {
        if (SkipTrigger("top.agents.host", (int)TopActionDebounce.TotalMilliseconds))
        {
            return;
        }

        try
        {
            if (Agents.IsHostRunning)
            {
                IsAgentsActionRunning = true;
                NotifyAgentsCommands();

                Agents.Reload();
                if (Agents.IsHostRunning)
                {
                    TryShowAgentsTopToast(() => _toasts.Success("Agents", "健康检查通过：Host 进程运行中"));
                }
                else
                {
                    TryShowAgentsTopToast(() => _toasts.Error("Agents", "健康检查失败：未检测到 Host 进程"));
                }
            }
            else
            {
                await RunAgentsCommandAsync(() => Agents.StartOrRestartAsync()).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.host_top_action.error", "Agents Host top action failed", ex);
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                NotifyAgentsCommands();
                RaiseAgentsStateChanged();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanControlAgents))]
    private async Task StartOrRestartModule(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return;
        }

        if (SkipTrigger($"top.agents.module:{moduleId}", (int)TopActionDebounce.TotalMilliseconds))
        {
            return;
        }

        try
        {
            var moduleLabel = Agents.Modules
                .FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal))
                ?.DisplayName;
            if (string.IsNullOrWhiteSpace(moduleLabel))
            {
                moduleLabel = moduleId;
            }

            if (!Agents.IsModuleEnabled(moduleId))
            {
                TryShowAgentsTopToast(() => _toasts.Error("Agents", $"{moduleLabel} 未启用"));
                return;
            }

            if (!Agents.IsHostRunning)
            {
                await RunAgentsCommandAsync(() => Agents.StartOrRestartAsync()).ConfigureAwait(false);
                return;
            }

            if (Agents.GetModuleState(moduleId) == AgentsRunState.Running)
            {
                IsAgentsActionRunning = true;
                NotifyAgentsCommands();

                Agents.Reload();
                if (Agents.GetModuleState(moduleId) == AgentsRunState.Running)
                {
                    TryShowAgentsTopToast(() => _toasts.Success("Agents", $"健康检查通过：{moduleLabel} 已就绪"));
                }
                else
                {
                    TryShowAgentsTopToast(() => _toasts.Error("Agents", $"健康检查失败：{moduleLabel} 未运行"));
                }

                return;
            }

            // Host 已运行时仅启动目标模块，保持其他模块和 Host 会话不变
            await RunAgentsCommandAsync(() => Agents.StartModuleAsync(moduleId)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "agents.module_top_action.error", "Agents module top action failed", ex, new { moduleId });
            TryShowAgentsTopToast(() => _toasts.Error("Agents", ex.Message));
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsAgentsActionRunning = false;
                NotifyAgentsCommands();
                RaiseAgentsStateChanged();
            });
        }
    }

    private async Task RunAgentsCommandAsync(Func<Task<AgentsCommandResult>> run)
    {
        await RunOnUiAsync(() =>
        {
            IsAgentsActionRunning = true;
            NotifyAgentsCommands();
        });

        var result = await run().ConfigureAwait(false);
        if (result.SuppressToast)
        {
            return;
        }

        if (result.Ok)
        {
            TryShowAgentsTopToast(() => _toasts.Success("Agents", result.Message));
        }
        else
        {
            TryShowAgentsTopToast(() => _toasts.Error("Agents", result.Message));
        }
    }

    private void NotifyAgentsCommands()
    {
        StartOrRestartHostCommand.NotifyCanExecuteChanged();
        StartOrRestartModuleCommand.NotifyCanExecuteChanged();
    }

    private void TryShowAgentsTopToast(Action show)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAgentsTopToastAt < AgentsTopToastDebounce)
        {
            return;
        }

        _lastAgentsTopToastAt = now;
        show();
    }

    private async Task<bool> CheckDbOnStartupAsync()
    {
        if (!File.Exists(_configPath))
        {
            return false;
        }

        _logger.Info("MainWindowVM", "db.startup_check.start", "Checking database connectivity on startup");
        var test = await _dbConnectionTester
            .TestAsync(_settings.AppliedDb, CancellationToken.None)
            .ConfigureAwait(false);
        var ok = test.Ok;
        if (!ok)
        {
            _logger.Warn("MainWindowVM", "db.startup_check.fail", "Database connection test failed on startup; shell banner will show status");
            return false;
        }

        try
        {
            await RunOnUiAsync(() => IsDbProbeRunning = true);
            var state = await GetDbSchemaStartupStateAsync().ConfigureAwait(false);
            if (!state.Compatible)
            {
                _toasts.Error("数据库版本不兼容", state.Message);
                var logContext = new
                {
                    desktopMin = state.DesktopMin,
                    desktopMax = state.DesktopMax,
                    agentsMin = state.AgentsMin,
                    agentsMax = state.AgentsMax,
                    target = state.Target,
                    dbVersion = state.DbVersion,
                    schemaOk = state.SchemaOk,
                    compatibility = state.Compatibility,
                    reason = state.Reason
                };

                if (state.SchemaOk && string.Equals(state.Compatibility, "BelowMinimum", StringComparison.Ordinal))
                {
                    _logger.Warn("MainWindowVM", "db.schema.external_update_required.startup",
                        "Database schema below app minimum; external update required before business access",
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

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "db.startup_check.fail", "Database startup check failed", ex);
            var openSettings = await _dialogs.Confirm(
                    "数据库检查失败",
                    $"数据库连接或结构检查失败：{ex.Message}\n请前往 [设置] 检查连接后重试")
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

    private DbSchemaVersionContext BuildSchemaContext()
    {
        var version = _releaseVersion.Current;
        return new DbSchemaVersionContext(
            version.DesktopMinDbSchema,
            version.DesktopMaxDbSchema,
            version.AgentsMinDbSchema,
            version.AgentsMaxDbSchema,
            version.DbSchemaVersion);
    }

    private async Task<DbSchemaStartupState> GetDbSchemaStartupStateAsync()
    {
        var version = _releaseVersion.Current;
        var context = BuildSchemaContext();
        var uiMin = DbSchemaCompat.NormalizeBound(version.DesktopMinDbSchema, version.DbSchemaVersion);
        var uiMax = DbSchemaCompat.NormalizeBound(version.DesktopMaxDbSchema, version.DbSchemaVersion);
        var agentsMin = DbSchemaCompat.NormalizeBound(version.AgentsMinDbSchema, version.DbSchemaVersion);
        var agentsMax = DbSchemaCompat.NormalizeBound(version.AgentsMaxDbSchema, version.DbSchemaVersion);
        var target = DbSchemaCompat.NormalizeBound(version.DbSchemaVersion, version.DbSchemaVersion);

        var snapshot = await _settings
            .GetSchemaStatusAsync(context, CancellationToken.None)
            .ConfigureAwait(false);

        if (snapshot.Satisfied)
        {
            _logger.Info("MainWindowVM", "db.schema.ok", "Database schema version compatible", new
            {
                schemaValue = snapshot.CurrentVersion,
                target,
                desktopMin = uiMin,
                desktopMax = uiMax,
                agentsMin,
                agentsMax
            });
        }
        else
        {
            _logger.Warn("MainWindowVM", "db.schema.incompatible", "Database schema incompatible", null, new
            {
                target,
                desktopMin = uiMin,
                desktopMax = uiMax,
                agentsMin,
                agentsMax,
                schemaOk = snapshot.SchemaOk,
                schemaValue = snapshot.CurrentVersion,
                schemaReason = snapshot.Reason,
                compatibility = snapshot.Compatibility.ToString()
            });
        }

        return new DbSchemaStartupState(
            Compatible: snapshot.Satisfied,
            Message: snapshot.IncompatibleMessage ?? "数据库版本不兼容",
            Target: snapshot.TargetVersion,
            DesktopMin: uiMin,
            DesktopMax: uiMax,
            AgentsMin: agentsMin,
            AgentsMax: agentsMax,
            SchemaOk: snapshot.SchemaOk,
            DbVersion: snapshot.CurrentVersion,
            Compatibility: snapshot.Compatibility.ToString(),
            Reason: snapshot.Reason);
    }

    private sealed record DbSchemaStartupState(
        bool Compatible,
        string Message,
        string Target,
        string DesktopMin,
        string DesktopMax,
        string AgentsMin,
        string AgentsMax,
        bool SchemaOk,
        string? DbVersion,
        string Compatibility,
        string? Reason);

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

    private void OnDbReconnectedRefreshSchema()
        => ObserveDetached(RefreshSchemaStatusAsync("db_reconnected"), "schema.refresh.detached.fail");

    private async Task RefreshSchemaStatusAsync(string source)
    {
        if (_settingsPage is not Settings settingsPage)
        {
            return;
        }

        try
        {
            await settingsPage.RefreshSchemaStatusAsync(source).ConfigureAwait(false);
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
            PostOnUi(RaiseConnectivityChanged);
        }
    }

    private void OnAgentsStatusChanged()
    {
        PostOnUi(() =>
        {
            RaiseAgentsStateChanged();
            NotifyAgentsCommands();
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

        ManageSchemaRecoveryPolling(shouldPoll: false);

        SafeExecute(() => _dbConfigNotifier.Applied -= OnDbConfigAppliedEvent);
        SafeExecute(() => _nav.NavigationRequested -= OnNavigationRequested);
        SafeExecute(() => _dbMonitor.ConnectionFailed -= ShowDbConnectionFailed);
        SafeExecute(() => _dbMonitor.Disconnected -= ShowDbDisconnected);
        SafeExecute(() => _dbMonitor.Reconnected -= ShowDbReconnectedInfo);
        SafeExecute(() => _dbMonitor.Reconnected -= OnDbReconnectedRefreshSchema);
        SafeExecute(() => _dbMonitor.Reconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _dbMonitor.Disconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _changeWatermark.TopicChanged -= OnTopicChanged);
        SafeExecute(() => Agents.StatusChanged -= OnAgentsStatusChanged);
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
