using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace pactoolkits_ui.Common;

public static class UiThreadHelper
{
    public static Task RunOnUiAsync(Action action)
        => RunOnUiAsync(action, DispatcherPriority.Background);

    public static async Task RunOnUiAsync(Action action, DispatcherPriority priority)
    {
        if (Dispatcher.UIThread.CheckAccess())
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, priority);
    }
}
