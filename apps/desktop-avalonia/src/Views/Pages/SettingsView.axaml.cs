using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class SettingsView : UserControl
{
    public static readonly StyledProperty<bool> IsClientAliasEditableProperty =
        AvaloniaProperty.Register<SettingsView, bool>(nameof(IsClientAliasEditable), false);

    public bool IsClientAliasEditable
    {
        get => GetValue(IsClientAliasEditableProperty);
        private set => SetValue(IsClientAliasEditableProperty, value);
    }

    private static readonly (string PageName, string Title, string Icon)[] TabNav =
    [
        ("TabDbPage", "PostgreSQL 设置", "Database"),
        ("TabAliasPage", "客户端别名映射", "Users"),
        ("TabTraceRulePage", "追溯码校验规则", "NotebookText"),
        ("TabUiBehaviorPage", "界面行为", "MonitorCog"),
        ("TabUpdatePage", "应用更新", "Download"),
        ("TabLoggingPage", "日志与诊断", "TextCursorInput"),
        ("TabMsfxPage", "码上放心 API", "Webhook"),
        ("TabAutomationPage", "自动化集成", "AudioWaveform")
    ];

    private Settings? _vm;
    private Panel? _contentHost;
    private StackPanel? _navItemsHost;
    private readonly List<TabLink> _tabLinks = new();
    private int _activeTabIndex;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnSettingsKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TryAttach(DataContext as Settings);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryAttach(DataContext as Settings);
    }

    private void TryAttach(Settings? vm)
    {
        if (ReferenceEquals(_vm, vm))
        {
            return;
        }

        _vm?.PropertyChanged -= OnVmPropertyChanged;
        _vm?.UnsavedChanged -= OnUnsavedChanged;

        _vm = vm;

        if (_vm is not null)
        {
            IsClientAliasEditable = !_vm.IsClientAliasReadOnly;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.UnsavedChanged += OnUnsavedChanged;
        }
        else
        {
            IsClientAliasEditable = false;
        }

        RefreshNavDots();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.IsClientAliasReadOnly))
        {
            IsClientAliasEditable = !(_vm?.IsClientAliasReadOnly ?? true);
        }

        if (e.PropertyName == nameof(Settings.HasUpdateAvailable))
        {
            RefreshNavDots();
        }
    }

    private void OnUnsavedChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshNavDots();
            return;
        }

        Dispatcher.UIThread.Post(RefreshNavDots);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        foreach (var link in _tabLinks)
        {
            link.NavButton.Click -= OnNavButtonClick;
        }

        _vm?.PropertyChanged -= OnVmPropertyChanged;
        _vm?.UnsavedChanged -= OnUnsavedChanged;

        _tabLinks.Clear();
        _contentHost = null;
        _navItemsHost = null;
        _vm = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryAttach(DataContext as Settings);
        Dispatcher.UIThread.Post(InitTabNav, DispatcherPriority.Loaded);
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter)
        {
            return;
        }

        var inputs = EnumerateTabInputs().ToList();
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private IEnumerable<Control> EnumerateTabInputs() =>
        InputFocusHelper.EnumerateInputs(this, typeof(TextBox), typeof(NumericUpDown), typeof(ComboBox));

    private void OnAutomationAddLineClicked(object? sender, RoutedEventArgs e)
    {
        var scroller = this.FindControl<ScrollViewer>("AutomationScrollViewer");
        var shouldAutoFollow = false;
        if (scroller is not null)
        {
            var remain = scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height);
            shouldAutoFollow = remain <= 28;
        }

        var targetName = (sender as Button)?.Tag as string;

        Dispatcher.UIThread.Post(() =>
        {
            if (shouldAutoFollow && scroller is not null)
            {
                var maxY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
                scroller.Offset = new Vector(scroller.Offset.X, maxY);
            }

            if (string.IsNullOrWhiteSpace(targetName))
            {
                return;
            }

            var itemsControl = this.FindControl<ItemsControl>(targetName);
            if (itemsControl is null)
            {
                return;
            }

            var targetBox = itemsControl.GetVisualDescendants().OfType<TextBox>().LastOrDefault();
            if (targetBox is null)
            {
                return;
            }

            targetBox.Focus();
            targetBox.CaretIndex = targetBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    private void OnNavButtonClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button navButton || navButton.Tag is null)
            {
                return;
            }

            var tabIndex = navButton.Tag switch
            {
                int i => i,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => -1
            };

            if (tabIndex < 0)
            {
                return;
            }

            TaskObserve.Observe(SwitchTabAsync(tabIndex), "SettingsView", "settings.tab_switch.detached.fail");
        }
        catch (Exception ex)
        {
            AppLog.Warn("SettingsView", "settings.nav_click.fail", "Navigation button handler failed", ex);
        }
    }

    private async Task SwitchTabAsync(int tabIndex)
    {
        if (tabIndex == _activeTabIndex)
        {
            return;
        }

        if (_vm is not null)
        {
            var ok = await _vm.ConfirmLeaveTabAsync(_activeTabIndex);
            if (!ok)
            {
                return;
            }
        }

        SetActiveTab(tabIndex);
    }

    private void InitTabNav()
    {
        if (_contentHost is not null && _tabLinks.Count > 0)
        {
            return;
        }

        _contentHost = this.FindControl<Panel>("ContentHost");
        _navItemsHost = this.FindControl<StackPanel>("NavItemsHost");

        if (_contentHost is null || _navItemsHost is null)
        {
            return;
        }

        _navItemsHost.Children.Clear();
        _tabLinks.Clear();
        BuildTabNav();
        SetActiveTab(0);
    }

    private void BuildTabNav()
    {
        if (_contentHost is null || _navItemsHost is null)
        {
            return;
        }

        var tabIndex = 0;
        foreach (var (pageName, title, icon) in TabNav)
        {
            var page = _contentHost.Children
                .OfType<Control>()
                .FirstOrDefault(child => string.Equals(child.Name, pageName, StringComparison.Ordinal));

            if (page is null)
            {
                continue;
            }

            var tracksUpdate = string.Equals(pageName, "TabUpdatePage", StringComparison.Ordinal);
            if (!CreateNavButton(
                    title,
                    icon,
                    tabIndex,
                    tracksUpdate,
                    out var navButton,
                    out var unsavedDot,
                    out var updateDot))
            {
                continue;
            }

            navButton.Click += OnNavButtonClick;

            _navItemsHost.Children.Add(navButton);
            _tabLinks.Add(new TabLink(
                navButton,
                page,
                unsavedDot,
                updateDot));
            tabIndex++;
        }
    }

    private static bool CreateNavButton(
        string title,
        string icon,
        int tabIndex,
        bool tracksUpdate,
        out Button navButton,
        out Border unsavedDot,
        out Border? updateDot)
    {
        navButton = null!;
        unsavedDot = null!;
        updateDot = null;

        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var content = new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };

        var iconHost = new Panel
        {
            Width = 20,
            Height = 20,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        iconHost.Children.Add(new AppIcon
        {
            Kind = icon,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom
        });

        if (tracksUpdate)
        {
            updateDot = new Border { IsVisible = false };
            updateDot.Classes.Add("UpdateDot");
            iconHost.Children.Add(updateDot);
        }
        content.Children.Add(iconHost);

        content.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        });

        unsavedDot = new Border { IsVisible = false };
        unsavedDot.Classes.Add("NavUnsavedDot");
        content.Children.Add(unsavedDot);

        navButton = new Button
        {
            Content = content,
            Tag = tabIndex,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };
        navButton.Classes.Add("NavItem");
        return true;
    }

    private void SetActiveTab(int index)
    {
        if (_tabLinks.Count == 0)
        {
            return;
        }

        if (index < 0 || index >= _tabLinks.Count)
        {
            return;
        }

        for (var i = 0; i < _tabLinks.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            var page = _tabLinks[i].Page;
            page.IsVisible = false;
            _tabLinks[i].NavButton.Classes.Set("Active", false);

            var scrollViewer = page.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer is not null)
            {
                SettingsScroll.ResetSticky(scrollViewer);
            }
        }

        var active = _tabLinks[index];
        active.Page.IsVisible = true;
        active.NavButton.Classes.Set("Active", true);

        var activeScroll = active.Page.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (activeScroll is not null)
        {
            SettingsScroll.ScheduleRefresh(activeScroll);
        }

        _activeTabIndex = index;
        RefreshNavDots();
    }

    private void RefreshNavDots()
    {
        if (_vm is null)
        {
            return;
        }

        for (var i = 0; i < _tabLinks.Count; i++)
        {
            _tabLinks[i].UnsavedDot.IsVisible = _vm.IsTabDirty(i);
            if (_tabLinks[i].UpdateDot is { } updateDot)
            {
                updateDot.IsVisible = _vm.HasUpdateAvailable;
            }
        }
    }

    private sealed record TabLink(
        Button NavButton,
        Control Page,
        Border UnsavedDot,
        Border? UpdateDot);
}
