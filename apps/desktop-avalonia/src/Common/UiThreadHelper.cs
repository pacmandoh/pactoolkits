using System;
using System.Threading.Tasks;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>UI 线程切换辅助</summary>
public static class UiThreadHelper
{
    public static Task RunOnUiAsync(Action action)
        => RunOnUiAsync(action, DispatcherPriority.Background);

    public static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.UIThread.CheckAccess() || !HasUiMessageLoop)
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action, priority);
    }

    public static void PostOnUi(Action action)
        => PostOnUi(action, DispatcherPriority.Background);

    public static void PostOnUi(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.UIThread.CheckAccess() || !HasUiMessageLoop)
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, priority);
    }

    private static bool HasUiMessageLoop => global::Avalonia.Application.Current is not null;
}
