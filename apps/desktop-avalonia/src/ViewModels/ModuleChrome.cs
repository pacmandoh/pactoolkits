using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.Common;

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
        Id = module.Id;
        DisplayName = module.DisplayName;
        ActiveIcon = module.Desktop.Icons.Active;
        InactiveIcon = module.Desktop.Icons.Inactive;
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ActiveIcon));
        OnPropertyChanged(nameof(InactiveIcon));
    }

    public void Apply(AgentsRunState state)
    {
        // 三态仅反映进程：Running 绿 / Starting 黄 / 其余红；断库由 Runtime 停模块后自然变红
        VisualState = state switch
        {
            AgentsRunState.Running => RuntimeVisualState.Active,
            AgentsRunState.Starting => RuntimeVisualState.Transitioning,
            _ => RuntimeVisualState.Inactive,
        };
        IsRunning = state is AgentsRunState.Running or AgentsRunState.Starting;
        Tip = state switch
        {
            AgentsRunState.Running => $"{DisplayName} · 运行中",
            AgentsRunState.Starting => $"{DisplayName} · 启动中",
            AgentsRunState.Failed => $"{DisplayName} · 失败",
            _ => $"{DisplayName} · 未启动",
        };
    }
}
