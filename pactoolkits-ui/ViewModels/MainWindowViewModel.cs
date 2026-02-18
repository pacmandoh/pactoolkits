using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Collections;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.DataAccess;
using pactoolkits_ui.Services;
using pactoolkits_ui.ViewModels.Pages;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Models;
using SukiUI.Toasts;

namespace pactoolkits_ui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public ISukiToastManager ToastManager { get; }
    public ISukiDialogManager DialogManager { get; }

    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IDbConfigService _dbConfig;
    private readonly IDbConnectionMonitorService _dbMonitor;
    private readonly IDbSchemaVersionService _dbSchemaVersion;
    private readonly IChangeWatermarkService _changeWatermark;
    private readonly IAhkRuntimeService _ahkRuntime;
    private readonly IReleaseVersionService _releaseVersion;
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
    private string? _lastDbFailReason;
    private string? _lastSeenConfigJson;
    private bool _dbEverDisconnected;

    private readonly TimeSpan _autoRefreshDebounce = TimeSpan.FromMilliseconds(180);
    private CancellationTokenSource? _autoRefreshCts;
    private CancellationTokenSource? _dbBootstrapCts;
    private CancellationTokenSource? _updatePollCts;
    private readonly object _dirtyPagesGate = new();
    private readonly HashSet<AppPageBase> _dirtyPages = new();

    private readonly SukiTheme _theme = SukiTheme.GetInstance();
    private IAvaloniaReadOnlyList<SukiColorTheme> Themes => _theme.ColorThemes;

    private IReadOnlyList<AppPageBase> Pages { get; }
    public IReadOnlyList<AppPageBase> SidebarPages { get; }
    private readonly Dictionary<Type, AppPageBase> _pageByType;
    private readonly AppPageBase? _settingsPage;
    private readonly AppPageBase? _aboutPage;
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
    [ObservableProperty] private string _currentUiVersion = "unknown";
    [ObservableProperty] private string _latestUiVersion = "unknown";
    public bool CanProbeDb() => !IsDbProbeRunning;
    public bool CanControlAhk() => !IsAhkActionRunning;

    public bool IsDbConnected => _dbMonitor.IsConnected;

    public string DbStatusTip
        => IsDbConnected ? "数据库：已连接" : "数据库：已断开";
    public string DbStatusText
        => IsDbConnected ? "已连接" : "已断开";

    public bool IsAhkRunning => _ahkRuntime.IsRunning;

    public string AhkStatusText
        => IsAhkRunning ? "运行中" : "未启动";

    public string UpdateStatusTip
        => HasUpdateAvailable
            ? $"发现新版本：{LatestUiVersion}（当前 {CurrentUiVersion}）"
            : "应用更新：当前已是最新版本";

    partial void OnHasUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(UpdateStatusTip));
    partial void OnCurrentUiVersionChanged(string value) => OnPropertyChanged(nameof(UpdateStatusTip));
    partial void OnLatestUiVersionChanged(string value) => OnPropertyChanged(nameof(UpdateStatusTip));

    private void RaiseDbStateChanged()
    {
        OnPropertyChanged(nameof(IsDbConnected));
        OnPropertyChanged(nameof(DbStatusTip));
        OnPropertyChanged(nameof(DbStatusText));
    }

    private void RaiseAhkStateChanged()
    {
        OnPropertyChanged(nameof(IsAhkRunning));
        OnPropertyChanged(nameof(AhkStatusText));
    }

    private static Task RunOnUiAsync(Action action)
        => RunOnUiAsync(action, DispatcherPriority.Background);

    private static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action, priority);
    }

    private static void PostOnUi(Action action)
        => PostOnUi(action, DispatcherPriority.Background);

    private static void PostOnUi(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, priority);
    }

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

        await ApplyUpdateFlowAsync().ConfigureAwait(false);
    }

    public MainWindowViewModel(
        IEnumerable<AppPageBase> pages,
        PageNavigationService nav,
        IToastService toasts,
        IDialogService dialogs,
        IDbConfigService dbConfig,
        ISukiToastManager toastManager,
        ISukiDialogManager dialogManager,
        IDbConnectionMonitorService dbMonitor,
        IDbSchemaVersionService dbSchemaVersion,
        IChangeWatermarkService changeWatermark,
        IAhkRuntimeService ahkRuntime,
        IReleaseVersionService releaseVersion,
        IAppUpdateService updates,
        IUpdateSettingsService updateSettings,
        IUpdateUiFlowService updateUiFlow,
        IAppLogger logger)
    {
        _toasts = toasts;
        _dialogs = dialogs;
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _dbMonitor = dbMonitor ?? throw new ArgumentNullException(nameof(dbMonitor));
        _dbSchemaVersion = dbSchemaVersion ?? throw new ArgumentNullException(nameof(dbSchemaVersion));
        _changeWatermark = changeWatermark ?? throw new ArgumentNullException(nameof(changeWatermark));
        _ahkRuntime = ahkRuntime ?? throw new ArgumentNullException(nameof(ahkRuntime));
        _releaseVersion = releaseVersion ?? throw new ArgumentNullException(nameof(releaseVersion));
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
        _changeWatermark.TopicChanged += OnWatermarkTopicChanged;

        _dbMonitor.Reconnected += ScheduleAutoRefresh;
        _dbMonitor.Disconnected += ScheduleAutoRefresh;
        _ahkRuntime.StatusChanged += OnAhkStatusChanged;
        _updates.Changed += OnUpdateChanged;
        _updateSettings.Changed += OnUpdateSettingsChanged;

        CurrentUiVersion = _updates.CurrentVersion;
        LatestUiVersion = _updates.LatestVersion;
        HasUpdateAvailable = _updates.HasUpdateAvailable;
        OnPropertyChanged(nameof(UpdateStatusTip));

        _ = CheckConfigOnStartupAsync();
        StartConfigWatcher();
        _ = CheckDatabaseOnStartupAsync();
        _dbMonitor.Start();
        _changeWatermark.Start();
        RaiseAhkStateChanged();

        StartDbStateBootstrap();
        _ = CheckUpdatesOnStartupAsync();
        RestartUpdatePolling();
        _logger.Info("MainWindowVM", "main.init", "Main window initialized");
    }

    private void SyncThemeState()
    {
        var variant = _theme.ActiveBaseTheme;

        if (variant == ThemeVariant.Default)
        {
            variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
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

        TryRefreshDirtyActivePage();
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
            _toasts.Error("数据库", report.Reason ?? "连接失败");
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
            if (_ahkRuntime.IsRunning)
            {
                _ahkRuntime.Reload();
                var running = _ahkRuntime.IsRunning;
                if (running)
                    TryShowAhkTopToast(() => _toasts.Success("追溯码自动注入工具", "健康检查通过：进程运行中"));
                else
                    TryShowAhkTopToast(() => _toasts.Error("追溯码自动注入工具", "健康检查失败：未检测到进程运行"));
            }
            else
            {
                var result = await _ahkRuntime.StartOrRestartAsync().ConfigureAwait(false);
                if (result.SuppressToast)
                    return;

                if (result.Ok)
                    TryShowAhkTopToast(() => _toasts.Success("追溯码自动注入工具", result.Message));
                else
                    TryShowAhkTopToast(() => _toasts.Error("追溯码自动注入工具", result.Message));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("MainWindowVM", "ahk.top_action.error", "AHK top action failed", ex);
            TryShowAhkTopToast(() => _toasts.Error("追溯码自动注入工具", ex.Message));
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

    private async Task CheckConfigOnStartupAsync()
    {
        if (File.Exists(_configPath)) return;

        var openSettings = await _dialogs.Confirm(
                "未找到配置文件",
                $"未检测到本地配置文件，请前往 [设置] 完成数据库连接配置后再使用")
            .ConfigureAwait(false);

        if (openSettings)
            await RunOnUiAsync(OpenSettings);
    }

    private async Task CheckDatabaseOnStartupAsync()
    {
        if (!File.Exists(_configPath)) return;

        _logger.Info("MainWindowVM", "db.startup_check.start", "Checking database connectivity on startup");
        var ok = await _dbConfig.TestConnectionAsync(_dbConfig.Current, CancellationToken.None).ConfigureAwait(false);

        if (ok)
        {
            var expected = _releaseVersion.Current.DbSchemaVersion;
            var schema = await _dbSchemaVersion.TryReadSchemaVersionAsync(CancellationToken.None).ConfigureAwait(false);
            if (!schema.ok)
            {
                _logger.Warn("MainWindowVM", "db.schema.read_fail", "Failed to read database schema version", null, new { schema.reason });
                _toasts.Warn("数据库 Schema", $"无法读取 schema_version：{schema.reason ?? "未知原因"}");
                return;
            }

            if (!string.Equals(schema.value, expected, StringComparison.Ordinal))
            {
                _logger.Warn("MainWindowVM", "db.schema.mismatch", "Database schema version mismatch", null, new { schema.value, expected });
                _toasts.Warn("数据库 Schema", $"版本不一致：DB={schema.value}，Manifest={expected}");
            }
            else
            {
                _logger.Info("MainWindowVM", "db.schema.ok", "Database schema version matched", new { schema.value, expected });
            }
            return;
        }

        var openSettings = await _dialogs.Confirm(
                "数据库未连接",
                "检测到已存在配置文件，但无法连接数据库请前往 [设置] 重新配置并测试连接")
            .ConfigureAwait(false);

        if (openSettings)
            await RunOnUiAsync(OpenSettings);
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!_updateSettings.Current.AutoCheckOnStartup)
            return;

        _logger.Info("MainWindowVM", "update.check.startup", "Auto checking updates on startup");
        await CheckAndPromptUpdateAsync(showNoUpdateToast: false, startupMode: true).ConfigureAwait(false);
    }

    private void RestartUpdatePolling()
    {
        _updatePollCts?.Cancel();
        _updatePollCts?.Dispose();
        _updatePollCts = null;

        if (!TryGetUpdatePollInterval(_updateSettings.Current, out var interval))
            return;

        _updatePollCts = new CancellationTokenSource();
        _ = RunUpdatePollingAsync(interval, _updatePollCts.Token);
    }

    private async Task RunUpdatePollingAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested || _disposed)
                return;

            await CheckAndPromptUpdateAsync(showNoUpdateToast: false, startupMode: false).ConfigureAwait(false);
        }
    }

    private static bool TryGetUpdatePollInterval(UpdateOptions options, out TimeSpan interval)
    {
        interval = TimeSpan.Zero;
        if (!options.AutoCheckOnStartup)
            return false;

        var configured = options.AutoCheckIntervalMinutes;
        if (configured > 0)
        {
            interval = TimeSpan.FromMinutes(Math.Clamp(configured, 1, 720));
            return true;
        }

        var channel = (options.Channel ?? string.Empty).Trim();
        var minutes = string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) ? 30 : 10;
        interval = TimeSpan.FromMinutes(minutes);
        return true;
    }

    private async Task CheckAndPromptUpdateAsync(bool showNoUpdateToast, bool startupMode)
    {
        if (IsUpdateChecking || IsUpdateApplying)
            return;

        await RunOnUiAsync(() => IsUpdateChecking = true);
        try
        {
            await _updateUiFlow.CheckAndHandleAsync(
                showNoUpdateToast: showNoUpdateToast,
                startupMode: startupMode,
                applyNowAction: ApplyUpdateFlowAsync,
                ignoreVersionAction: IgnoreCurrentUpdateAsync,
                syncState: result =>
                {
                    CurrentUiVersion = result.CurrentVersion;
                    LatestUiVersion = result.LatestVersion;
                    HasUpdateAvailable = result.HasUpdate;
                },
                logScope: "MainWindowVM").ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsUpdateChecking = false);
        }
    }

    private async Task ApplyUpdateFlowAsync()
    {
        if (IsUpdateApplying)
            return;

        await RunOnUiAsync(() => IsUpdateApplying = true);
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
            await RunOnUiAsync(() => IsUpdateApplying = false);
        }
    }

    private Task IgnoreCurrentUpdateAsync()
        => _updateUiFlow.IgnoreVersionAsync(LatestUiVersion);

    private void StartConfigWatcher()
    {
        try
        {
            Directory.CreateDirectory(_configDir);

            // Watch the unified app config and react to external edits.
            _configWatcher = new FileSystemWatcher(_configDir, _configFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
            };

            _configWatcher.Changed += OnConfigWatcherChanged;
            _configWatcher.Created += OnConfigWatcherChanged;
            _configWatcher.Renamed += OnConfigWatcherRenamed;
            _configWatcher.EnableRaisingEvents = true;
        }
        catch (System.Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.watcher.init_fail", "Failed to initialize config watcher", ex);
        }
    }

    private async Task OnConfigChangedAsync()
    {
        if (_disposed) return;
        if (_isApplyingConfig) return;

        // Reason: Debounce file watcher bursts from editor write patterns.
        await Task.Delay(350).ConfigureAwait(false);

        if (!File.Exists(_configPath)) return;
        _ahkRuntime.Reload();

        AppConfigRoot? loaded;
        string? json;
        try
        {
            json = await File.ReadAllTextAsync(_configPath).ConfigureAwait(false);

            if (string.Equals(json, _lastSeenConfigJson, StringComparison.Ordinal))
                return;

            loaded = JsonSerializer.Deserialize<AppConfigRoot>(json);
        }
        catch
        {
            loaded = null;
            json = null;
        }

        if (loaded?.Postgres is null) return;

        if (IsSamePgOptions(_dbConfig.Current, loaded.Postgres))
        {
            _lastSeenConfigJson = json;
            return;
        }

        try
        {
            // External config change is applied through the same save/apply pipeline.
            _isApplyingConfig = true;
            _lastSeenConfigJson = json;
            await _dbConfig.SaveAndApplyAsync(loaded.Postgres).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            _logger.Warn("MainWindowVM", "config.external_apply_fail", "Failed to apply external config changes", ex);
        }
        finally
        {
            _isApplyingConfig = false;
        }

        _dbMonitor.Signal();
    }

    private static bool IsSamePgOptions(PgOptions a, PgOptions b)
        => string.Equals(a.Host, b.Host, StringComparison.Ordinal)
           && a.Port == b.Port
           && string.Equals(a.Database, b.Database, StringComparison.Ordinal)
           && string.Equals(a.Username, b.Username, StringComparison.Ordinal)
           && string.Equals(a.Password, b.Password, StringComparison.Ordinal)
           && a.ConnectTimeoutSeconds == b.ConnectTimeoutSeconds
           && a.CommandTimeoutSeconds == b.CommandTimeoutSeconds
           && a.PoolSize == b.PoolSize
           && a.ReconnectIntervalSeconds == b.ReconnectIntervalSeconds
           && a.KeepAliveSeconds == b.KeepAliveSeconds
           && a.MonitorPingSeconds == b.MonitorPingSeconds
           && a.MonitorPingTimeoutSeconds == b.MonitorPingTimeoutSeconds;

    private void OnConfigWatcherChanged(object? sender, FileSystemEventArgs e)
        => _ = OnConfigChangedAsync();

    private void OnConfigWatcherRenamed(object? sender, RenamedEventArgs e)
        => _ = OnConfigChangedAsync();

    private void OnDbConfigAppliedEvent(object? sender, EventArgs e)
        => OnDbConfigApplied();

    private void OnDbConfigApplied()
    {
        _dbMonitor.Signal();

        RaiseDbStateChanged();

        if (_dbMonitor.IsConnected)
            ScheduleAutoRefresh();
    }

    private void ScheduleAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = new CancellationTokenSource();
        _ = RunAutoRefreshAsync(_autoRefreshCts.Token);
    }

    private async Task RunAutoRefreshAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_autoRefreshDebounce, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        // Reason: Refresh runs on the UI thread because page commands touch bindings.
        PostOnUi(RefreshActiveAndMarkOthersDirty);
    }

    private void RefreshActiveAndMarkOthersDirty()
    {
        try
        {
            var active = ActivePage;

            foreach (var p in Pages)
            {
                if (!CanRefreshPage(p))
                    continue;

                if (!ReferenceEquals(p, active))
                {
                    MarkPageDirty(p);
                }
            }

            if (active is not null && CanRefreshPage(active))
            {
                if (TryRefreshPage(active))
                    ClearDirty(active);
                else
                    MarkPageDirty(active);
            }
        }
        catch (System.Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.refresh.batch_fail", "Batch refresh failed", ex);
        }
    }

    private void TryRefreshDirtyActivePage()
    {
        var active = ActivePage;
        if (active is null) return;
        if (!CanRefreshPage(active)) return;
        if (!IsDirty(active)) return;

        if (TryRefreshPage(active))
            ClearDirty(active);
    }

    private static bool CanRefreshPage(AppPageBase page)
        => page is not ISettingsPage
           && page is ITopBarActions { RefreshCommand: not null };

    private static bool TryRefreshPage(AppPageBase page)
    {
        if (page is not ITopBarActions top || top.RefreshCommand is not { } cmd)
            return false;

        if (!cmd.CanExecute(null))
            return false;

        cmd.Execute(null);
        return true;
    }

    private void MarkPageDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
            _dirtyPages.Add(page);
    }

    private bool IsDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
            return _dirtyPages.Contains(page);
    }

    private void ClearDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
            _dirtyPages.Remove(page);
    }

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

    private void OnWatermarkTopicChanged(string topic)
    {
        PostOnUi(() =>
        {
            var skipInventoryRefresh = ActivePage is InventoryOverviewViewModel inv
                                       && inv.ShouldDeferExternalRefreshForTopic(topic);

            MarkPagesDirtyByTopic(topic, skipInventoryRefresh);
            if (ShouldRefreshActiveImmediatelyForTopic(topic)
                && !(skipInventoryRefresh && ActivePage is InventoryOverviewViewModel))
                TryRefreshDirtyActivePage();
        });
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
            CurrentUiVersion = _updates.CurrentVersion;
            LatestUiVersion = _updates.LatestVersion;
            HasUpdateAvailable = _updates.HasUpdateAvailable;
            OnPropertyChanged(nameof(UpdateStatusTip));
        });
    }

    private void OnUpdateSettingsChanged()
        => RestartUpdatePolling();

    private bool ShouldRefreshActiveImmediatelyForTopic(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        if (key != "drug_index")
            return true;

        if (ActivePage is DrugIndexViewModel)
            return false;

        return true;
    }

    private void MarkPagesDirtyByTopic(string? topic, bool skipInventoryPage)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();

        switch (key)
        {
            case "drug_index":
                MarkDirtyByType<DrugIndexViewModel>();
                MarkDirtyByType<DashboardViewModel>();
                break;

            case "inventory":
            case "trace_pool":
            case "trace_txn":
            case "trace_txn_item":
                if (!skipInventoryPage)
                    MarkDirtyByType<InventoryOverviewViewModel>();
                MarkDirtyByType<DashboardViewModel>();
                break;

            default:
                foreach (var page in Pages)
                {
                    if (CanRefreshPage(page))
                        MarkPageDirty(page);
                }
                break;
        }
    }

    private void MarkDirtyByType<TPage>() where TPage : AppPageBase
    {
        if (_pageByType.TryGetValue(typeof(TPage), out var page) && CanRefreshPage(page))
            MarkPageDirty(page);
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
        SafeExecute(() => _dbMonitor.Reconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _dbMonitor.Disconnected -= ScheduleAutoRefresh);
        SafeExecute(() => _changeWatermark.TopicChanged -= OnWatermarkTopicChanged);
        SafeExecute(() => _ahkRuntime.StatusChanged -= OnAhkStatusChanged);
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
