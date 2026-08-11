using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Ui.Threading;
using PacToolkits.Desktop.Avalonia.ViewModels;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Views;

public partial class MainWindowShellStatusBar
{
    private const string HostToggleTag = "__host__";

    private MainWindowViewModel? _vm;
    private string _agentsBarStructureKey = string.Empty;
    private bool _agentsBarSuppressToggle;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        HookAgentsBar(DataContext as MainWindowViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookAgentsBar(DataContext as MainWindowViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        HookAgentsBar(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void HookAgentsBar(MainWindowViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
        {
            return;
        }

        if (_vm is not null)
        {
            _vm.AgentsChromeUpdated -= OnAgentsChromeUpdated;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = vm;

        if (_vm is not null)
        {
            _vm.AgentsChromeUpdated += OnAgentsChromeUpdated;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Dispatcher.UIThread.Post(RebuildAgentsBarMenu, DispatcherPriority.Loaded);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CanControlAgents))
        {
            UpdateAgentsBarToggleStates();
        }
    }

    private void OnAgentsChromeUpdated()
        => Dispatcher.UIThread.Post(() =>
        {
            if (!TryGetAgentsBarRoot(out _))
            {
                return;
            }

            var key = BuildAgentsBarStructureKey(_vm);
            if (!string.Equals(key, _agentsBarStructureKey, StringComparison.Ordinal))
            {
                RebuildAgentsBarMenu();
            }
            else
            {
                UpdateAgentsBarToggleStates();
            }
        });

    private void RebuildAgentsBarMenu()
    {
        if (_vm is null || !TryGetAgentsBarRoot(out var root))
        {
            return;
        }

        _agentsBarStructureKey = BuildAgentsBarStructureKey(_vm);
        _agentsBarSuppressToggle = true;
        try
        {
            root.Items.Clear();

            root.Items.Add(BuildHostToggleItem(_vm));
            root.Items.Add(new MenuItem
            {
                Header = CreateMenuBodyText("配置"),
                Command = _vm.OpenAgentsSettingsCommand
            });
            root.Items.Add(new Separator());
            root.Items.Add(BuildSectionLabelItem("模块"));

            foreach (var module in _vm.AgentsMenuModules.ToList())
            {
                root.Items.Add(BuildModuleSubmenu(module, _vm));
            }
        }
        finally
        {
            _agentsBarSuppressToggle = false;
        }

        UpdateAgentsBarToggleStates();
    }

    private MenuItem BuildHostToggleItem(MainWindowViewModel vm)
    {
        // 与模块开关一致：code-behind 写 IsChecked，避免 TwoWay 在启动失败后卡在「开」
        var toggle = CreateToggleSwitch();
        toggle.Tag = HostToggleTag;
        toggle.IsChecked = vm.IsHostMenuChecked;
        toggle.IsEnabled = vm.CanControlAgents;
        toggle.IsCheckedChanged += OnHostMenuToggleChanged;

        return new MenuItem
        {
            StaysOpenOnClick = true,
            Header = BuildToggleHeader("状态", toggle)
        };
    }

    private MenuItem BuildModuleSubmenu(ModuleChrome module, MainWindowViewModel vm)
    {
        var toggle = CreateToggleSwitch();
        toggle.Tag = module.Id;
        toggle.IsChecked = module.IsRunning;
        toggle.IsCheckedChanged += OnModuleMenuToggleChanged;

        var submenu = new MenuItem
        {
            Header = CreateMenuBodyText(module.DisplayName)
        };
        MenuItemAssist.SetPopupPlacement(submenu, PlacementMode.RightEdgeAlignedTop);
        MenuItemAssist.SetPopupHorizontalOffset(submenu, 4);
        submenu.Items.Add(new MenuItem
        {
            StaysOpenOnClick = true,
            Header = BuildToggleHeader("状态", toggle)
        });
        submenu.Items.Add(new MenuItem
        {
            Header = CreateMenuBodyText("配置"),
            Command = vm.OpenModuleSettingsCommand,
            CommandParameter = module.Id
        });
        return submenu;
    }

    private static MenuItem BuildSectionLabelItem(string text)
    {
        var item = new MenuItem
        {
            Header = CreateSectionLabel(text),
            IsEnabled = false,
            Focusable = false
        };
        item.Classes.Add("MenuSectionLabel");
        return item;
    }

    private static TextBlock CreateSectionLabel(string text)
        => new()
        {
            Text = text,
            Classes = { "Caption", "Muted" }
        };

    private static TextBlock CreateMenuBodyText(string text)
        => new()
        {
            Text = text,
            Classes = { "Small" },
            VerticalAlignment = VerticalAlignment.Center
        };

    private static ToggleSwitch CreateToggleSwitch()
        => new()
        {
            OnContent = "启动",
            OffContent = "关闭",
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Control BuildToggleHeader(string title, ToggleSwitch toggle)
    {
        DockPanel.SetDock(toggle, Dock.Right);
        var label = CreateMenuBodyText(title);
        label.Margin = new Thickness(0, 0, 12, 0);
        return new DockPanel
        {
            MinWidth = 200,
            LastChildFill = true,
            Children =
            {
                toggle,
                label
            }
        };
    }

    private void OnHostMenuToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_agentsBarSuppressToggle || sender is not ToggleSwitch { Tag: string tag } toggle
            || !string.Equals(tag, HostToggleTag, StringComparison.Ordinal))
        {
            return;
        }

        if (_vm is null)
        {
            return;
        }

        var wantRunning = toggle.IsChecked == true;
        TaskObserve.Observe(
            _vm.SetHostRunningAsync(wantRunning),
            "MainWindowShellStatusBar",
            "agents.host_menu_toggle.detached.fail");
    }

    private void OnModuleMenuToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_agentsBarSuppressToggle || sender is not ToggleSwitch { Tag: string moduleId } toggle
            || string.Equals(moduleId, HostToggleTag, StringComparison.Ordinal))
        {
            return;
        }

        if (_vm is null)
        {
            return;
        }

        var wantRunning = toggle.IsChecked == true;
        TaskObserve.Observe(
            _vm.SetModuleRunningAsync(moduleId, wantRunning),
            "MainWindowShellStatusBar",
            "agents.module_menu_toggle.detached.fail");
    }

    private void UpdateAgentsBarToggleStates()
    {
        if (_vm is null || !TryGetAgentsBarRoot(out var root))
        {
            return;
        }

        _agentsBarSuppressToggle = true;
        try
        {
            foreach (var item in root.Items.OfType<MenuItem>())
            {
                if (FindToggle(item) is { Tag: string tag } hostToggle
                    && string.Equals(tag, HostToggleTag, StringComparison.Ordinal))
                {
                    // Runtime 真相写回；失败后强制回「关」
                    hostToggle.IsChecked = _vm.IsHostMenuChecked;
                    hostToggle.IsEnabled = _vm.CanControlAgents;
                }
            }

            foreach (var moduleMenu in EnumerateModuleMenus(root))
            {
                foreach (var child in moduleMenu.Items.OfType<MenuItem>())
                {
                    if (FindToggle(child) is not { Tag: string moduleId } toggle
                        || string.Equals(moduleId, HostToggleTag, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var match = _vm.AgentsMenuModules.FirstOrDefault(m =>
                        string.Equals(m.Id, moduleId, StringComparison.Ordinal));
                    if (match is null)
                    {
                        continue;
                    }

                    toggle.IsChecked = match.IsRunning;
                    toggle.IsEnabled = _vm.CanControlAgents;
                }
            }
        }
        finally
        {
            _agentsBarSuppressToggle = false;
        }
    }

    private static IEnumerable<MenuItem> EnumerateModuleMenus(MenuItem root)
    {
        foreach (var item in root.Items.OfType<MenuItem>())
        {
            if (item.Items.OfType<MenuItem>().Any(child => FindToggle(child) is not null))
            {
                yield return item;
            }
        }
    }

    private static ToggleSwitch? FindToggle(MenuItem item)
        => item.Header is DockPanel panel
            ? panel.Children.OfType<ToggleSwitch>().FirstOrDefault()
            : null;

    private bool TryGetAgentsBarRoot(out MenuItem root)
    {
        var found = this.FindControl<MenuItem>("AgentsBarRoot");
        if (found is null)
        {
            root = null!;
            return false;
        }

        root = found;
        return true;
    }

    private static string BuildAgentsBarStructureKey(MainWindowViewModel? vm)
    {
        if (vm is null)
        {
            return string.Empty;
        }

        return string.Join(
            '|',
            vm.AgentsMenuModules.Select(m => $"{m.Id}:{m.DisplayName}"));
    }
}
