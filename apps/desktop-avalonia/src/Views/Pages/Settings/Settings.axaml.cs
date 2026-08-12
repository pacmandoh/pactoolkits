using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Ui.Threading;
using ModuleSettingsFieldViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.ModuleSettingsFieldViewModel;
using SettingsLineItem = PacToolkits.Desktop.Avalonia.ViewModels.Pages.SettingsLineItem;
using SettingsViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Settings;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class Settings : UserControl
{
    public static readonly StyledProperty<bool> IsClientAliasEditableProperty =
        AvaloniaProperty.Register<Settings, bool>(nameof(IsClientAliasEditable), false);

    public bool IsClientAliasEditable
    {
        get => GetValue(IsClientAliasEditableProperty);
        private set => SetValue(IsClientAliasEditableProperty, value);
    }

    private static readonly (string PageName, string Title, string Icon)[] TabNav =
    [
        ("TabDbPage", "连接设置", "Server"),
        ("TabAliasPage", "客户端别名映射", "Users"),
        ("TabTraceRulePage", "追溯码校验规则", "Regex"),
        ("TabUiBehaviorPage", "界面行为", "MonitorCog"),
        ("TabUpdatePage", "应用更新", "CloudDownload"),
        ("TabLoggingPage", "日志与诊断", "TextCursorInput"),
        ("TabMsfxPage", "码上放心 API", "Webhook"),
        ("TabAgentsPage", "自动化集成", "AudioWaveform"),
        ("TabModuleSettingsPage", "模块配置", "SlidersHorizontal")
    ];

    private SettingsViewModel? _vm;
    private Panel? _contentHost;
    private StackPanel? _navItemsHost;
    private readonly List<TabLink> _tabLinks = new();
    private int _activeTabIndex;
    private string? _moduleSettingsModuleId;

    public Settings()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        TryAttach(DataContext as SettingsViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryAttach(DataContext as SettingsViewModel);
    }

    private void TryAttach(SettingsViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
        {
            return;
        }

        _vm?.PropertyChanged -= OnVmPropertyChanged;
        _vm?.UnsavedChanged -= OnUnsavedChanged;
        _vm?.TabOpenRequested -= OnTabOpenRequested;

        _vm = vm;
        _moduleSettingsModuleId = vm?.SelectedModuleEditor?.ModuleId;

        if (_vm is not null)
        {
            IsClientAliasEditable = !_vm.IsClientAliasReadOnly;
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.UnsavedChanged += OnUnsavedChanged;
            _vm.TabOpenRequested += OnTabOpenRequested;
        }
        else
        {
            IsClientAliasEditable = false;
        }

        RefreshNavDots();
    }

    private void OnTabOpenRequested(int tabIndex)
    {
        // 导航未 Init 时仅保留 VM pending，Init 后再切
        if (_tabLinks.Count == 0)
        {
            return;
        }

        TaskObserve.Observe(SwitchTabAsync(tabIndex), "Settings", "settings.tab_open_requested.detached.fail");
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsClientAliasReadOnly))
        {
            IsClientAliasEditable = !(_vm?.IsClientAliasReadOnly ?? true);
        }

        if (e.PropertyName == nameof(SettingsViewModel.HasUpdateAvailable))
        {
            RefreshNavDots();
        }

        if (e.PropertyName == nameof(SettingsViewModel.SelectedModuleEditor))
        {
            OnSelectedModuleEditorChanged();
        }
    }

    // 仅模块配置内部 Tab 换模块时回顶；同模块重载或左侧目录切换不改 Offset
    private void OnSelectedModuleEditorChanged()
    {
        var nextId = _vm?.SelectedModuleEditor?.ModuleId;
        if (nextId is null)
        {
            _moduleSettingsModuleId = null;
            return;
        }

        if (string.Equals(_moduleSettingsModuleId, nextId, StringComparison.Ordinal))
        {
            return;
        }

        var previousId = _moduleSettingsModuleId;
        _moduleSettingsModuleId = nextId;
        if (previousId is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(ResetModuleSettingsScroll, DispatcherPriority.Background);
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
        _vm?.TabOpenRequested -= OnTabOpenRequested;

        _tabLinks.Clear();
        _contentHost = null;
        _navItemsHost = null;
        _vm = null;
        _moduleSettingsModuleId = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TryAttach(DataContext as SettingsViewModel);
        Dispatcher.UIThread.Post(InitTabNav, DispatcherPriority.Loaded);
    }

    private void OnModuleAddLineClicked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var field = (sender as Button)?.DataContext as ModuleSettingsFieldViewModel;
        if (field is null)
        {
            return;
        }

        var scroller = this.FindControl<ScrollViewer>("ModuleSettingsScrollViewer");
        var shouldAutoFollow = scroller is not null
                               && scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height) <= 28;

        Dispatcher.UIThread.Post(() =>
        {
            if (shouldAutoFollow && scroller is not null)
            {
                var maxY = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
                scroller.Offset = new Vector(scroller.Offset.X, maxY);
            }

            var targetBox = this.GetVisualDescendants()
                .OfType<TextBox>()
                .LastOrDefault(box => box.DataContext is SettingsLineItem item
                                      && field.ListItems.Contains(item));
            if (targetBox is null)
            {
                return;
            }

            targetBox.Focus();
            targetBox.CaretIndex = targetBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    private void ResetModuleSettingsScroll()
    {
        var scroller = this.FindControl<ScrollViewer>("ModuleSettingsScrollViewer");
        if (scroller is null)
        {
            return;
        }

        scroller.Offset = new Vector(scroller.Offset.X, 0);
        SettingsScroll.ResetSticky(scroller);
        SettingsScroll.ScheduleRefresh(scroller);
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

            TaskObserve.Observe(SwitchTabAsync(tabIndex), "Settings", "settings.tab_switch.detached.fail");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Settings", "settings.nav_click.fail", "Navigation button handler failed", ex);
        }
    }

    private async Task SwitchTabAsync(int tabIndex)
    {
        // 已在目标 Tab：仍可能 deep-link 换模块选择；脏页先确认离开
        if (tabIndex == _activeTabIndex)
        {
            if (_vm is not null && _vm.IsTabDirty(tabIndex))
            {
                var stay = await _vm.ConfirmLeaveTabAsync(tabIndex);
                if (!stay)
                {
                    _vm.CancelPendingOpen();
                    return;
                }
            }

            _vm?.OnTabEntered(tabIndex);
            return;
        }

        if (_vm is not null)
        {
            var ok = await _vm.ConfirmLeaveTabAsync(_activeTabIndex);
            if (!ok)
            {
                _vm.CancelPendingOpen();
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

        if (_vm is not null && _vm.TryTakePendingOpenTab(out var pending))
        {
            SetActiveTab(pending);
        }
        else
        {
            SetActiveTab(0);
        }
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
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
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
        _vm?.OnTabEntered(index);
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
