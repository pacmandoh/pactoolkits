using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Common;
using ShadWindow = ShadUI.Window;

namespace PacToolkits.Desktop.Avalonia.Views;

/// <summary>
/// 主窗口：标题栏居中导航、Windows 最大化 chrome、macOS 退出路径
/// </summary>
public partial class MainWindow : ShadWindow
{
    private TitleBarCentering? _titleBarCentering;
    private WindowState _fullScreenRestoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        PopupDismissHelper.AttachTopLevel(this);

        // Avalonia 12.1 起不再对 BorderOnly 自定义框强制 DWM 圆角；显式恢复 Win11 系统圆角
        if (OperatingSystem.IsWindows())
        {
            Win32Properties.SetWindowCornerPreference(
                this,
                Win32Properties.WindowCornerPreference.Round);
        }

        FullscreenButton.Click += OnFullScreen;
        SyncExpandButton();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _titleBarCentering?.Dispose();
        _titleBarCentering = null;

        var titleBarBackground = e.NameScope.Find<Border>("PART_TitleBarBackground");
        AttachWindowsTitleBarMark(titleBarBackground);

        var titlePanel = e.NameScope.Find<StackPanel>("AppTitlePanel");
        if (titlePanel is null)
        {
            return;
        }

        // ShadUI 对 LogoContent 关闭命中测试；此槽位承载居中导航控件
        titlePanel.IsHitTestVisible = true;
        foreach (var child in titlePanel.Children)
        {
            if (child is ContentPresenter presenter)
            {
                presenter.IsHitTestVisible = true;
                break;
            }
        }

        if (titleBarBackground is not null)
        {
            _titleBarCentering = new TitleBarCentering(titlePanel, TitleBarCenterAnchor, titleBarBackground);
        }
    }

    private void AttachWindowsTitleBarMark(Border? titleBarBackground)
    {
        if (!OperatingSystem.IsWindows() || titleBarBackground is null)
        {
            return;
        }

        // ShadUI 没有独立的左侧标题栏槽位，背景层用于保持居中路径不受两侧内容影响
        if (Resources["WindowsTitleBarMarkTemplate"] is IDataTemplate template)
        {
            titleBarBackground.Child = template.Build(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _titleBarCentering?.Dispose();
        _titleBarCentering = null;
        base.OnClosed(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            TrackFullScreenRestore(change);
            SyncMaximizedChrome();
            SyncExpandButton();
        }
    }

    private void TrackFullScreenRestore(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.NewValue is WindowState.FullScreen &&
            change.OldValue is WindowState previous &&
            previous != WindowState.FullScreen)
        {
            _fullScreenRestoreState = previous == WindowState.Minimized ? WindowState.Normal : previous;
        }
    }

    private void SyncExpandButton()
    {
        if (FullscreenButton is null)
        {
            return;
        }

        var tip = WindowState switch
        {
            WindowState.FullScreen => "退出全屏",
            WindowState.Maximized => "还原",
            _ when OperatingSystem.IsWindows() => "最大化",
            _ => "全屏",
        };
        ToolTip.SetTip(FullscreenButton, tip);
    }

    private void SyncMaximizedChrome()
    {
        if (WindowState == WindowState.Maximized)
        {
            // 最大化时去掉自绘圆角，避免 ClipToBounds 裁进标题按钮
            RootCornerRadius = default;

            if (OperatingSystem.IsWindows())
            {
                // Avalonia 12 将 OffScreenMargin 置零；ShadUI SnapLayout 接管 WM_NCCALCSIZE 后
                // 跳过 Avalonia BorderOnly 的最大化客户区收缩，避免内容延伸到屏幕外边框
                Margin = WindowsMaximizedFrameInset(DesktopScaling);
            }

            return;
        }

        // 本地赋值会盖住 XAML 绑定；还原时 ClearValue 才能回到 RootCornerRadius 资源
        ClearValue(RootCornerRadiusProperty);

        if (OperatingSystem.IsWindows())
        {
            ClearValue(MarginProperty);
        }
    }

    [SupportedOSPlatform("windows")]
    private static Thickness WindowsMaximizedFrameInset(double scaling)
    {
        const int smCxFrame = 32;
        const int smCyFrame = 33;
        const int smCxPaddedBorder = 92;

        var pad = GetSystemMetrics(smCxPaddedBorder);
        var scale = scaling <= 0 ? 1 : scaling;
        var x = (GetSystemMetrics(smCxFrame) + pad) / scale;
        var y = (GetSystemMetrics(smCyFrame) + pad) / scale;
        return new Thickness(x, y, x, y);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private void OnFullScreen(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsMacOS())
        {
            ToggleMacFullScreen();
            return;
        }

        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _fullScreenRestoreState;
        }
        else if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else if (OperatingSystem.IsWindows())
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.FullScreen;
        }
    }

    [SupportedOSPlatform("macos")]
    private void ToggleMacFullScreen()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? 0;
        if (handle == 0)
        {
            return;
        }

        // AppKit 程序化退出前会恢复窗口装饰，因此复用 macOS 窗口关闭按钮的退出路径
        var selector = GetMacSelector("toggleFullScreen:");
        SendMacMessage(handle, selector, 0);
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern nint GetMacSelector(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendMacMessage(nint receiver, nint selector, nint sender);
}
