using System;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private readonly TriggerDebounceGate _triggerDebounce = new();

    protected bool ShouldSkipTrigger(
        int milliseconds = 1200,
        [CallerMemberName] string key = "")
        => _triggerDebounce.ShouldSkip(key, TimeSpan.FromMilliseconds(milliseconds));

    protected bool ShouldSkipTrigger(
        string key,
        int milliseconds = 1200)
        => _triggerDebounce.ShouldSkip(key, TimeSpan.FromMilliseconds(milliseconds));
}
