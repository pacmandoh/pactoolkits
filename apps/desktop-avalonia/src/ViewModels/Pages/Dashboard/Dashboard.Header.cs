using CommunityToolkit.Mvvm.Input;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class Dashboard
{
    public string FilterBarToggleIconKind => IsFilterBarVisible ? "FunnelX" : "Funnel";
    public string FilterBarToggleToolTip => IsFilterBarVisible ? "隐藏筛选栏" : "显示筛选栏";

    [RelayCommand]
    private void ToggleFilterBar() =>
        IsFilterBarVisible = !IsFilterBarVisible;

    partial void OnIsFilterBarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(FilterBarToggleIconKind));
        OnPropertyChanged(nameof(FilterBarToggleToolTip));
    }
}
