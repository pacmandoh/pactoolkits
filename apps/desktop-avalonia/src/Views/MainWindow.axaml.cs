using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadWindow = ShadUI.Window;

namespace PacToolkits.Desktop.Avalonia.Views;

public partial class MainWindow : ShadWindow
{
    private TitleBarCentering? _titleBarCentering;

    public MainWindow()
    {
        InitializeComponent();
        DialogHostPolicy.DisableBackgroundDismiss(this);
        PopupDismissHelper.AttachTopLevel(this);

        ToolTip.SetTip(FullscreenButton, "全屏");
        FullscreenButton.Click += OnFullScreen;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _titleBarCentering?.Dispose();
        _titleBarCentering = null;

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

        var titleBarBackground = e.NameScope.Find<Border>("PART_TitleBarBackground");
        if (titleBarBackground is not null)
        {
            _titleBarCentering = new TitleBarCentering(titlePanel, TitleBarCenterAnchor, titleBarBackground);
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
            SyncMaximizedChrome();
        }
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
        if (WindowState == WindowState.FullScreen)
        {
            ExitFullScreen();
            ToolTip.SetTip(FullscreenButton, "全屏");
        }
        else
        {
            WindowState = WindowState.FullScreen;
            ToolTip.SetTip(FullscreenButton, "退出全屏");
        }
    }
}
