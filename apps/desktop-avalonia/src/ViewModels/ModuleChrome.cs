using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// Desktop 壳层模块项（顶栏 pills / 底栏 status bar）；图标与显隐来自 module.json desktop
/// </summary>
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
    private bool _isRunning;

    [ObservableProperty]
    private bool _isStarting;

    [ObservableProperty]
    private bool _isInactive = true;

    [ObservableProperty]
    private string _statusText = "未知";

    public string ItemText => $"{DisplayName}：{StatusText}";

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
        OnPropertyChanged(nameof(ItemText));
    }

    public void Apply(AgentsRunState state)
    {
        IsRunning = state == AgentsRunState.Running;
        IsStarting = state == AgentsRunState.Starting;
        IsInactive = !state.IsActive();
        StatusText = state switch
        {
            AgentsRunState.Running => "运行中",
            AgentsRunState.Starting => "启动中",
            AgentsRunState.Failed => "启动失败",
            AgentsRunState.Stopped => "未启动",
            _ => "未知",
        };
    }

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(ItemText));
}
