using System;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Desktop.Avalonia.Ui.State;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// 为 Desktop ViewModel 提供属性通知和统一的命令重复触发抑制
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private readonly TriggerDebounceGate _triggerDebounce = new();

    protected bool SkipTrigger(
        int milliseconds = 1200,
        [CallerMemberName] string key = "")
        => _triggerDebounce.Skip(key, TimeSpan.FromMilliseconds(milliseconds));

    protected bool SkipTrigger(
        string key,
        int milliseconds = 1200)
        => _triggerDebounce.Skip(key, TimeSpan.FromMilliseconds(milliseconds));
}
