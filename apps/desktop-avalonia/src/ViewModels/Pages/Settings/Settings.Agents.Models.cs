using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

/// <summary>
/// 模块集合字段中的单个可编辑值
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
/// 列映射行：locked 时仅 headers 可改（id / 必填 / 整数 / 移除锁定）
/// </summary>
public sealed partial class SettingsColFieldItem : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;

    [ObservableProperty] private string _label = string.Empty;

    [ObservableProperty] private string _headersText = string.Empty;

    [ObservableProperty] private bool _required = true;

    [ObservableProperty] private bool _asInt;

    public SettingsColFieldItem(string id, string label, IReadOnlyList<string> headers, bool required, bool asInt, bool isLocked)
    {
        _id = (id ?? string.Empty).Trim();
        _label = (label ?? string.Empty).Trim();
        _headersText = string.Join(" / ", headers);
        _required = required;
        _asInt = asInt;
        IsLocked = isLocked;
    }

    public bool IsLocked { get; }

    public bool CanEdit => !IsLocked;

    /// <summary>
    /// 展示名：有 label 用 label，否则露出 id
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            var label = (Label ?? string.Empty).Trim();
            return label.Length > 0 ? label : (Id ?? string.Empty).Trim();
        }
    }

    partial void OnLabelChanged(string value)
        => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnIdChanged(string value)
        => OnPropertyChanged(nameof(DisplayTitle));

    public IReadOnlyList<string> ParseHeaders()
        => (HeadersText ?? string.Empty)
            .Split(['/', ',', '，', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => part.Length > 0)
            .ToArray();

    public bool SameAs(SettingsColFieldItem other)
        => string.Equals(Id, other.Id, StringComparison.Ordinal)
           && string.Equals(Label, other.Label, StringComparison.Ordinal)
           && string.Equals(HeadersText, other.HeadersText, StringComparison.Ordinal)
           && Required == other.Required
           && AsInt == other.AsInt
           && IsLocked == other.IsLocked;

    internal static string NewUniqueId(IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 10_000; i++)
        {
            var candidate = "col_" + Guid.NewGuid().ToString("N")[..8];
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        return "col_" + Guid.NewGuid().ToString("N");
    }
}

/// <summary>
/// 模块运行控制、状态和版本信息的设置页展示模型
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
/// 模块设置表单加载失败时的展示信息
/// </summary>
public sealed record ModuleSettingsLoadIssue(string DisplayName, string Message);
