using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Platform;
using global::Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.ViewModels;
using PacToolkits.Desktop.Avalonia.Views;

namespace PacToolkits.Desktop.Avalonia;

public partial class App : global::Avalonia.Application
{
    public IServiceProvider Services { get; private set; } = default!;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private IUiBehaviorService? _uiBehavior;
    private IAgentsManager? _agentsManager;
    private IAppLogger? _logger;
    private UnlockActivity? _unlockActivity;
    private bool _shutdownCleanupDone;
    private UnhandledExceptionEventHandler? _appDomainUnhandledHandler;
    private EventHandler<UnobservedTaskExceptionEventArgs>? _taskUnhandledHandler;
    private DispatcherUnhandledExceptionEventHandler? _uiUnhandledHandler;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "PACTOOLKITS_")
            .Build();

        var services = new ServiceCollection();
        services.AddPacToolkitsUiServices(config);

        Services = services.BuildServiceProvider();

        DataTemplates.Add(new ViewLocator(Services.GetRequiredService<AppViews>()));

        _uiBehavior = Services.GetRequiredService<IUiBehaviorService>();
        _agentsManager = Services.GetRequiredService<IAgentsManager>();
        _logger = Services.GetRequiredService<IAppLogger>();
        var releaseVersion = Services.GetRequiredService<IReleaseVersionService>().Current;
        _logger.Info("App", "app.start", "Application startup", new
        {
            releaseVersion.DesktopVersion,
            releaseVersion.ProductVersion,
            releaseVersion.AgentsVersion,
            releaseVersion.BuildChannel,
            releaseVersion.BuildDate
        });
        RegisterGlobalExceptionHandlers();

        _mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainWindowViewModel>()
        };
        _unlockActivity = Services.GetRequiredService<UnlockActivity>();
        _unlockActivity.Attach(_mainWindow);
        _mainWindow.Closing += OnMainWindowClosing;

        desktop.MainWindow = _mainWindow;
        Program.Instance?.SetActivationHandler(ActivateMainWindow);
        try
        {
            BuildTrayIcon(_mainWindow);
        }
        catch
        {
            _trayIcon = null;
        }
        _logger.Info("App", "app.ready", "Main window initialized");

        base.OnFrameworkInitializationCompleted();
    }

    private void BuildTrayIcon(MainWindow window)
    {
        if (!TryGetResource("TrayMenu", null, out var menuResource) || menuResource is not NativeMenu menu)
        {
            throw new InvalidOperationException("TrayMenu resource not found.");
        }

        if (menu.Items.Count != 5 ||
            menu.Items[0] is not NativeMenuItem showItem ||
            menu.Items[1] is not NativeMenuItem trayModeItem ||
            menu.Items[2] is not NativeMenuItem aboutItem ||
            menu.Items[3] is not NativeMenuItemSeparator ||
            menu.Items[4] is not NativeMenuItem exitItem)
        {
            throw new InvalidOperationException("TrayMenu resource shape is invalid.");
        }

        showItem.Click += (_, _) => ShowMainWindow(window);

        UpdateTrayModeHeader(trayModeItem, _uiBehavior?.Current.MinimizeToTrayOnClose ?? true);
        trayModeItem.Click += (_, _) =>
        {
            var current = _uiBehavior?.Current.MinimizeToTrayOnClose ?? true;
            var next = !current;
            UpdateTrayModeHeader(trayModeItem, next);
            TaskObserve.Observe(PersistTrayModeAsync(next, trayModeItem), "App", "tray.persist.detached.fail");
        };

        aboutItem.Click += (_, _) =>
        {
            // Native 菜单收起完成后再让 ShadUI 打开托管对话框
            Dispatcher.UIThread.Post(() =>
            {
                ShowMainWindow(window);
                if (window.DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ShowAppInfoCommand.Execute(null);
                }
            }, DispatcherPriority.Background);
        };

        // 不走 desktop.Shutdown：Avalonia HandleClosed 拆树会踩 #13497/#14437
        exitItem.Click += (_, _) => RequestProcessExit();

        _trayIcon = new TrayIcon
        {
            ToolTipText = "PacToolkits",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PacToolkits.Desktop/Assets/app.ico"))),
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => ShowMainWindow(window);

        _uiBehavior?.Changed += () =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateTrayModeHeader(trayModeItem, _uiBehavior.Current.MinimizeToTrayOnClose);
                });
            };
    }

    private async System.Threading.Tasks.Task PersistTrayModeAsync(bool enabled, NativeMenuItem trayModeItem)
    {
        if (_uiBehavior is null)
        {
            return;
        }

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
                UpdateTrayModeHeader(trayModeItem, fallback);
            });
        }
    }

    private static void UpdateTrayModeHeader(NativeMenuItem item, bool enabled)
        => item.Header = $"关闭行为：{(enabled ? "后台" : "退出")}";

    private void ShowMainWindow(MainWindow window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void ActivateMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is not null)
            {
                ShowMainWindow(_mainWindow);
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var allowClose = e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown
            || _uiBehavior?.Current.MinimizeToTrayOnClose != true
            || _trayIcon is null;

        if (!allowClose)
        {
            if (sender is MainWindow window)
            {
                e.Cancel = true;
                window.Hide();
            }

            return;
        }

        // 取消原生关窗拆树，改为清理后 Environment.Exit（规避 Avalonia #13497/#14437）
        e.Cancel = true;
        RequestProcessExit();
    }

    // 业务清理后结束进程，跳过 TopLevel.HandleClosed 的 VisualTree 拆卸
    private void RequestProcessExit()
    {
        try
        {
            RunShutdownCleanup();
        }
        catch (Exception ex)
        {
            try
            {
                _logger?.Warn("App", "shutdown.cleanup_fail", "Shutdown cleanup failed before process exit", ex);
            }
            catch
            {
                // 退出路径：日志失败也必须继续 Exit
            }
        }

        Environment.Exit(0);
    }

    private void RunShutdownCleanup()
    {
        if (_shutdownCleanupDone)
        {
            return;
        }

        _shutdownCleanupDone = true;

        _mainWindow?.Closing -= OnMainWindowClosing;

        _unlockActivity?.Dispose();
        _unlockActivity = null;

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        UnregisterGlobalExceptionHandlers();

        // 真正退出应用：确保已注册的 Host 进程全部停掉
        if (_agentsManager is not null)
        {
            try
            {
                _agentsManager.StopAllAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.Warn("App", "shutdown.agents_stop_fail", "Failed to stop registered Agents runtimes during shutdown", ex);
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

    private void RegisterGlobalExceptionHandlers()
    {
        _uiUnhandledHandler = (_, e) =>
        {
            _logger?.Fatal("App", "unhandled.desktop", "Unhandled desktop exception", e.Exception);
        };
        global::Avalonia.Threading.Dispatcher.UIThread.UnhandledException += _uiUnhandledHandler;

        _taskUnhandledHandler = (_, e) =>
        {
            _logger?.Fatal("App", "unobserved.task", "Unobserved task exception", e.Exception);
            e.SetObserved();
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
            global::Avalonia.Threading.Dispatcher.UIThread.UnhandledException -= _uiUnhandledHandler;
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
