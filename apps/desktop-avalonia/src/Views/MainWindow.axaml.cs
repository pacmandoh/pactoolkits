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
    }

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
