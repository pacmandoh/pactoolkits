using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PacToolkits.Desktop.Avalonia.Views;

/// <summary>主窗右上标题栏胶囊：主题、扩展、DB、Agents、模块</summary>
public partial class MainWindowTitleBarCapsule : UserControl
{
    public static readonly DirectProperty<MainWindowTitleBarCapsule, bool> IsHostExpandedProperty =
        AvaloniaProperty.RegisterDirect<MainWindowTitleBarCapsule, bool>(
            nameof(IsHostExpanded),
            o => o.IsHostExpanded);

    private Window? _host;
    private bool _isHostExpanded;

    public MainWindowTitleBarCapsule()
    {
        InitializeComponent();
        ExpandButton.Click += (_, e) => ExpandRequested?.Invoke(this, e);
    }

    /// <summary>由宿主 Window 处理全屏/最大化切换</summary>
    public event EventHandler<RoutedEventArgs>? ExpandRequested;

    public bool IsHostExpanded
    {
        get => _isHostExpanded;
        private set => SetAndRaise(IsHostExpandedProperty, ref _isHostExpanded, value);
    }

    public void SetExpandTip(string tip)
        => ToolTip.SetTip(ExpandButton, tip);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookHost(TopLevel.GetTopLevel(this) as Window);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        HookHost(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void HookHost(Window? host)
    {
        if (!ReferenceEquals(_host, host))
        {
            _host?.PropertyChanged -= OnHostPropertyChanged;
            _host = host;
            if (_host is null)
            {
                return;
            }

            _host.PropertyChanged += OnHostPropertyChanged;
            SyncHostExpanded();
        }
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            SyncHostExpanded();
        }
    }

    private void SyncHostExpanded()
    {
        if (_host is null)
        {
            return;
        }

        IsHostExpanded = _host.WindowState is WindowState.Maximized or WindowState.FullScreen;
    }
}
