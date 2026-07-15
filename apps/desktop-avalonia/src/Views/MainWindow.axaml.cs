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
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadWindow = ShadUI.Window;

namespace PacToolkits.Desktop.Avalonia.Views;

public partial class MainWindow : ShadWindow
{
    private TitleBarCentering? _titleBarCentering;
    private WindowState _fullScreenRestoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        DialogHostPolicy.DisableBackgroundDismiss(this);
        PopupDismissHelper.AttachTopLevel(this);

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

        // ShadUI disables hit testing for LogoContent; this slot hosts the centered navigation controls.
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

        // ShadUI has no independent left title-bar slot; the background layer keeps the centered path undisturbed.
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
            // ShadUI restores RootCornerRadius on Maximize; ClipToBounds then rounds into caption buttons.
            RootCornerRadius = default;

            if (OperatingSystem.IsWindows())
            {
                // Avalonia 12 zeros OffScreenMargin; ShadUI SnapLayout marks WM_NCCALCSIZE handled and
                // skips Avalonia's BorderOnly maximize client shrink — content paints into the off-screen frame.
                Margin = WindowsMaximizedFrameInset(DesktopScaling);
            }

            return;
        }

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

        // Avalonia restores decorations before AppKit finishes a programmatic exit; use the traffic-light path.
        var selector = GetMacSelector("toggleFullScreen:");
        SendMacMessage(handle, selector, 0);
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern nint GetMacSelector(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendMacMessage(nint receiver, nint selector, nint sender);
}
