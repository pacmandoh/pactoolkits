using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using pactoolkits_ui.Common;
using pactoolkits_ui.DataAccess;
using pactoolkits_ui.Repositories;
using pactoolkits_ui.Services.Application;
using pactoolkits_ui.Services.Infrastructure;
using pactoolkits_ui.Services.Integration;
using pactoolkits_ui.ViewModels;
using pactoolkits_ui.ViewModels.Pages;
using pactoolkits_ui.Views;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace pactoolkits_ui;

public class App : Application
{
    public IServiceProvider Services { get; private set; } = default!;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private IUiBehaviorService? _uiBehavior;
    private IAhkRuntimeService? _ahkRuntime;
    private IAppLogger? _logger;
    private bool _forceExit;
    private EventHandler? _themeChangedHandler;
    private UnhandledExceptionEventHandler? _appDomainUnhandledHandler;
    private EventHandler<UnobservedTaskExceptionEventArgs>? _taskUnhandledHandler;
    private DispatcherUnhandledExceptionEventHandler? _uiUnhandledHandler;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        DisableAvaloniaDataAnnotationValidation();

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "PACTOOLKITS_")
            .Build();

        var services = new ServiceCollection();

        services.AddSingleton<IAppConfigStore, AppConfigStore>();
        services.AddSingleton<IUiBehaviorService, UiBehaviorService>();
        services.AddSingleton<ILoggingSettingsService, LoggingSettingsService>();
        services.AddSingleton<IUpdateSettingsService, UpdateSettingsService>();
        services.AddSingleton<IDbConfigService, DbConfigService>();
        services.AddSingleton<ITraceCodeRuleService, TraceCodeRuleService>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<PageNavigationService>();
        services.AddSingleton<AppViews>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<SukiToastManager>();
        services.AddSingleton<ISukiToastManager>(sp => sp.GetRequiredService<SukiToastManager>());

        services.AddSingleton<SukiDialogManager>();
        services.AddSingleton<ISukiDialogManager>(sp => sp.GetRequiredService<SukiDialogManager>());

        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<IReleaseVersionService, ReleaseVersionService>();
        services.AddSingleton<IAppStartupStateService, AppStartupStateService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IUpdateUiFlowService, UpdateUiFlowService>();
        services.AddSingleton<IDbSchemaVersionService, DbSchemaVersionService>();
        services.AddSingleton<IDbSchemaMigrationService, DbSchemaMigrationService>();
        services.AddSingleton<ITraceEntryLogService, TraceEntryLogService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISensitiveOperationUnlockService, SensitiveOperationUnlockService>();
        services.AddSingleton<IDbConnectionMonitorService, DbConnectionMonitorService>();
        services.AddSingleton<IChangeWatermarkService, ChangeWatermarkService>();
        services.AddSingleton<IDbConnectionTester, DbConnectionTester>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAhkRuntimeService, AhkRuntimeService>();
        services.AddSingleton<IDrugIndexRepo, DrugIndexRepo>();
        services.AddSingleton<IScanCodeRepo, ScanCodeRepo>();
        services.AddSingleton<IDashboardRepo, DashboardRepo>();
        services.AddSingleton<ILookupCatalogService, LookupCatalogService>();
        services.AddSingleton<ClientAliasStore>();
        services.AddSingleton<IClientAliasService, ClientAliasService>();
        services.AddSingleton<IClientIdReadRepo, ClientIdReadRepo>();
        services.AddSingleton<IInventoryOverviewRepo, InventoryOverviewRepo>();
        services.AddSingleton<InventoryOverviewViewModel>();
        services.AddSingleton<IMsfxApiClient, MsfxApiClient>();
        services.AddSingleton<IMsfxSyncRepo, MsfxSyncRepo>();

        AddAllPages(services);

        services.AddPostgres(config);

        Services = services.BuildServiceProvider();

        DataTemplates.Add(new ViewLocator(Services.GetRequiredService<AppViews>()));

        _uiBehavior = Services.GetRequiredService<IUiBehaviorService>();
        _ahkRuntime = Services.GetRequiredService<IAhkRuntimeService>();
        _logger = Services.GetRequiredService<IAppLogger>();
        var releaseVersion = Services.GetRequiredService<IReleaseVersionService>().Current;
        Resources["AppVersionText"] = $"PacToolkits v{releaseVersion.UiVersion}";
        Resources["AppChannelText"] = string.IsNullOrWhiteSpace(releaseVersion.BuildChannel)
            ? "stable"
            : releaseVersion.BuildChannel.Trim().ToLowerInvariant();
        _logger.Info("App", "app.start", "Application startup", new
        {
            releaseVersion.UiVersion,
            releaseVersion.SuiteVersion,
            releaseVersion.BuildChannel,
            releaseVersion.BuildDate
        });
        RegisterGlobalExceptionHandlers();

        _mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };
        _mainWindow.Closing += OnMainWindowClosing;

        desktop.MainWindow = _mainWindow;
        try
        {
            BuildTrayIcon(_mainWindow, desktop);
        }
        catch
        {
            _trayIcon = null;
        }
        desktop.Exit += OnDesktopExit;
        _logger.Info("App", "app.ready", "Main window initialized");

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTrayIcon(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!TryGetResource("TrayMenu", null, out var menuResource) || menuResource is not NativeMenu menu)
            throw new InvalidOperationException("TrayMenu resource not found.");

        if (menu.Items.Count < 4 ||
            menu.Items[0] is not NativeMenuItem showItem ||
            menu.Items[1] is not NativeMenuItem trayModeItem ||
            menu.Items[3] is not NativeMenuItem exitItem)
        {
            throw new InvalidOperationException("TrayMenu resource shape is invalid.");
        }

        UpdateActionMenuIcons(showItem, exitItem);
        showItem.Click += (_, _) => ShowMainWindow(window);

        trayModeItem.IsChecked = _uiBehavior?.Current.MinimizeToTrayOnClose ?? true;
        trayModeItem.Click += (_, _) =>
        {
            var current = _uiBehavior?.Current.MinimizeToTrayOnClose ?? true;
            var next = !current;
            trayModeItem.IsChecked = next;
            _ = PersistTrayModeAsync(next, trayModeItem);
        };

        exitItem.Click += (_, _) =>
        {
            _forceExit = true;
            desktop.Shutdown();
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "PacToolkits",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://pactoolkits-ui/Assets/app.ico"))),
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => ShowMainWindow(window);

        _themeChangedHandler = (_, _) =>
        {
            Dispatcher.UIThread.Post(() => UpdateActionMenuIcons(showItem, exitItem));
        };
        ActualThemeVariantChanged += _themeChangedHandler;

        if (_uiBehavior is not null)
        {
            _uiBehavior.Changed += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    trayModeItem.IsChecked = _uiBehavior.Current.MinimizeToTrayOnClose;
                });
            };
        }
    }

    private async System.Threading.Tasks.Task PersistTrayModeAsync(bool enabled, NativeMenuItem trayModeItem)
    {
        if (_uiBehavior is null)
            return;

        try
        {
            await _uiBehavior.SaveAsync(new UiBehaviorOptions
            {
                MinimizeToTrayOnClose = enabled
            }).ConfigureAwait(false);
        }
        catch
        {
            var fallback = _uiBehavior.Current.MinimizeToTrayOnClose;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                trayModeItem.IsChecked = fallback;
            });
        }
    }

    private static Bitmap? LoadMenuIcon(string uri)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private void UpdateActionMenuIcons(NativeMenuItem showItem, NativeMenuItem exitItem)
    {
        var dark = IsDarkThemeActive();
        var showUri = dark
            ? "avares://pactoolkits-ui/Assets/open-in-app-dark.png"
            : "avares://pactoolkits-ui/Assets/open-in-app.png";
        var exitUri = dark
            ? "avares://pactoolkits-ui/Assets/exit-to-app-dark.png"
            : "avares://pactoolkits-ui/Assets/exit-to-app.png";

        showItem.Icon = LoadMenuIcon(showUri);
        exitItem.Icon = LoadMenuIcon(exitUri);
    }

    private bool IsDarkThemeActive()
    {
        var variant = ActualThemeVariant;
        if (variant == ThemeVariant.Default)
            variant = RequestedThemeVariant;

        return variant == ThemeVariant.Dark;
    }

    private void ShowMainWindow(MainWindow window)
    {
        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Activate();
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceExit ||
            e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
            return;

        if (_uiBehavior?.Current.MinimizeToTrayOnClose != true)
            return;

        if (_trayIcon is null)
            return;

        if (sender is MainWindow window)
        {
            e.Cancel = true;
            window.Hide();
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit -= OnDesktopExit;

        if (_themeChangedHandler is not null)
        {
            ActualThemeVariantChanged -= _themeChangedHandler;
            _themeChangedHandler = null;
        }

        if (_mainWindow is not null)
            _mainWindow.Closing -= OnMainWindowClosing;

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        UnregisterGlobalExceptionHandlers();

        // Real app exit: ensure the external AHK injector process is stopped.
        if (_ahkRuntime is not null)
        {
            try
            {
                _ahkRuntime.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.Warn("App", "shutdown.ahk_stop_fail", "Failed to stop AHK runtime during shutdown", ex);
            }
        }

        _logger?.Info("App", "app.shutdown", "Application shutdown");

        try
        {
            (Services as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Warn("App", "shutdown.services_dispose_fail", "Failed to dispose service provider", ex);
        }
    }

    private static void AddAllPages(IServiceCollection services)
    {
        var asm = typeof(AppPageBase).Assembly;

        var pageTypes = asm.GetTypes()
            .Where(t => !t.IsAbstract
                        && typeof(AppPageBase).IsAssignableFrom(t));

        foreach (var t in pageTypes)
        {
            services.AddSingleton(t);
            services.AddSingleton(typeof(AppPageBase), sp => (AppPageBase)sp.GetRequiredService(t));
        }
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var toRemove = BindingPlugins.DataValidators
            .OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        foreach (var plugin in toRemove)
            BindingPlugins.DataValidators.Remove(plugin);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        _uiUnhandledHandler = (_, e) =>
        {
            _logger?.Fatal("App", "unhandled.ui", "Unhandled UI exception", e.Exception);
        };
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += _uiUnhandledHandler;

        _taskUnhandledHandler = (_, e) =>
        {
            _logger?.Fatal("App", "unobserved.task", "Unobserved task exception", e.Exception);
        };
        TaskScheduler.UnobservedTaskException += _taskUnhandledHandler;

        _appDomainUnhandledHandler = (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger?.Fatal("App", "unhandled.appdomain", "Unhandled AppDomain exception", ex);
        };
        AppDomain.CurrentDomain.UnhandledException += _appDomainUnhandledHandler;
    }

    private void UnregisterGlobalExceptionHandlers()
    {
        if (_uiUnhandledHandler is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= _uiUnhandledHandler;
            _uiUnhandledHandler = null;
        }

        if (_taskUnhandledHandler is not null)
        {
            TaskScheduler.UnobservedTaskException -= _taskUnhandledHandler;
            _taskUnhandledHandler = null;
        }

        if (_appDomainUnhandledHandler is not null)
        {
            AppDomain.CurrentDomain.UnhandledException -= _appDomainUnhandledHandler;
            _appDomainUnhandledHandler = null;
        }
    }
}
