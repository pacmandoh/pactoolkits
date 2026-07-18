using CommunityToolkit.Mvvm.Input;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink
{
    public bool ShowQueueSearchHeaderAction =>
        IsQueuePage && IsQueueMonitorWorkspace;

    public bool ShowMappingConfigHeaderAction =>
        IsQueuePage && IsMappingWorkspace;

    public string RunConfigBarToggleIconKind =>
        IsRunConfigBarVisible ? "PanelTopClose" : "SlidersHorizontal";

    public string QueueSearchBarToggleIconKind =>
        IsQueueSearchBarVisible ? "SearchX" : "Search";

    public string MappingConfigBarToggleIconKind =>
        IsMappingConfigBarVisible ? "PanelTopClose" : "SlidersHorizontal";

    public string RunConfigBarToggleToolTip =>
        IsRunConfigBarVisible ? "隐藏配置条" : "显示配置条";

    public string QueueSearchBarToggleToolTip =>
        IsQueueSearchBarVisible ? "隐藏搜索栏" : "显示搜索栏";

    public string MappingConfigBarToggleToolTip =>
        IsMappingConfigBarVisible ? "隐藏配置条" : "显示配置条";

    [RelayCommand]
    private void ToggleRunConfigBar() =>
        IsRunConfigBarVisible = !IsRunConfigBarVisible;

    [RelayCommand]
    private void ToggleQueueSearchBar()
    {
        if (IsMappingWorkspace)
        {
            return;
        }

        if (IsTaskQueueBatchModeActive)
        {
            _toast.Warn("批量操作", "请先确认或取消当前批量操作");
            return;
        }

        IsQueueSearchBarVisible = !IsQueueSearchBarVisible;
    }

    [RelayCommand]
    private void ToggleMappingConfigBar()
    {
        if (!IsMappingWorkspace)
        {
            return;
        }

        if (IsMappingBusy)
        {
            _toast.Warn("批量映射", "请先确认或取消当前批量映射操作");
            return;
        }

        IsMappingConfigBarVisible = !IsMappingConfigBarVisible;
    }

    partial void OnIsRunConfigBarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(RunConfigBarToggleIconKind));
        OnPropertyChanged(nameof(RunConfigBarToggleToolTip));
    }
}
