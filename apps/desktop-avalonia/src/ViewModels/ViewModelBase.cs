using System;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

/// <summary>
/// MVVM 基类：ObservableObject + 命令触发防抖
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
