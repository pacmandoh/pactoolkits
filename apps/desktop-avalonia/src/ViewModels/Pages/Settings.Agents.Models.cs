using CommunityToolkit.Mvvm.ComponentModel;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// Schema 表单中 stringList / stringFlagMap 的可编辑行
/// </summary>
public sealed partial class SettingsLineItem : ObservableObject
{
    [ObservableProperty] private string _value = string.Empty;

    public SettingsLineItem()
    {
    }

    public SettingsLineItem(string value)
    {
        _value = value;
    }
}

/// <summary>
/// Settings Agents 页：按发现模块动态生成的运行控制 / 版本信息行
/// </summary>
public sealed partial class ModuleRunRow : ObservableObject
{
    public ModuleRunRow(string id, string displayName)
    {
        Id = id;
        _displayName = displayName;
    }

    public string Id { get; }

    [ObservableProperty] private string _displayName = string.Empty;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isRunningSwitch;
    [ObservableProperty] private bool _canToggle = true;
    [ObservableProperty] private string _statusText = "检测中";
    [ObservableProperty] private string _statusDetail = "等待模块状态刷新";
    [ObservableProperty] private string _versionText = "未知";
    [ObservableProperty] private string _lastLaunchText = "-";
    [ObservableProperty] private string _lastErrorText = "-";
    [ObservableProperty] private bool _isStatusUnknown = true;
    [ObservableProperty] private bool _isStatusStarting;
    [ObservableProperty] private bool _isStatusRunning;
    [ObservableProperty] private bool _isStatusFailed;
    [ObservableProperty] private bool _isStatusStopped;
}

/// <summary>
/// 无法生成模块设置表单时展示的显式错误态
/// </summary>
public sealed record ModuleSettingsLoadIssue(string DisplayName, string Message);
