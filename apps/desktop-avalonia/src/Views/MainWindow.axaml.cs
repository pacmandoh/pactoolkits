using Avalonia.Controls;
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
