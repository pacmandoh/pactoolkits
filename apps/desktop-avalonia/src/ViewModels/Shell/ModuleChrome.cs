using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>模块在标题栏胶囊与底栏 Agents 菜单中的展示投影</summary>
public sealed partial class ModuleChrome : ObservableObject
{
    public ModuleChrome(ModuleDescriptor module)
    {
        ApplyDescriptor(module);
    }

    public string Id { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string ActiveIcon { get; private set; } = string.Empty;

    public string InactiveIcon { get; private set; } = string.Empty;

    [ObservableProperty]
    private RuntimeVisualState _visualState = RuntimeVisualState.Inactive;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _tip = string.Empty;

    public void ApplyDescriptor(ModuleDescriptor module)
    {
        var id = module.Id;
        var displayName = module.DisplayName;
        var activeIcon = module.Desktop.Icons.Active;
        var inactiveIcon = module.Desktop.Icons.Inactive;

        if (!string.Equals(Id, id, StringComparison.Ordinal))
        {
            Id = id;
            OnPropertyChanged(nameof(Id));
        }

        if (!string.Equals(DisplayName, displayName, StringComparison.Ordinal))
        {
            DisplayName = displayName;
            OnPropertyChanged(nameof(DisplayName));
        }

        if (!string.Equals(ActiveIcon, activeIcon, StringComparison.Ordinal))
        {
            ActiveIcon = activeIcon;
            OnPropertyChanged(nameof(ActiveIcon));
        }

        if (!string.Equals(InactiveIcon, inactiveIcon, StringComparison.Ordinal))
        {
            InactiveIcon = inactiveIcon;
            OnPropertyChanged(nameof(InactiveIcon));
        }
    }

    public void Apply(AgentsRunState state)
    {
        // 灯色：Running 绿 / Starting 黄 / Failed·Stopped 红
        VisualState = state switch
        {
            AgentsRunState.Running => RuntimeVisualState.Active,
            AgentsRunState.Starting => RuntimeVisualState.Transitioning,
            _ => RuntimeVisualState.Inactive,
        };
        // 开关仅 Running 为开（与 Settings 一致，失败/Starting 不为开）
        IsRunning = state == AgentsRunState.Running;
        Tip = state switch
        {
            AgentsRunState.Running => $"{DisplayName} · 运行中",
            AgentsRunState.Starting => $"{DisplayName} · 启动中",
            AgentsRunState.Failed => $"{DisplayName} · 失败",
            _ => $"{DisplayName} · 未启动",
        };
    }
}
