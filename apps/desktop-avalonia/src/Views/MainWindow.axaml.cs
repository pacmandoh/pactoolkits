using PacToolkits.Desktop.Avalonia.Common;
using SukiUI.Controls;

namespace PacToolkits.Desktop.Avalonia.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        ContextMenuDismissTracker.AttachTopLevel(this);
    }
}
