using System;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public interface IBackgroundTaskRunner
{
    /// <summary>
    /// Starts a fire-and-forget task on the caller's synchronization context.
    /// Exceptions are logged; cancellation is silent.
    /// </summary>
    void RunDetached(
        Func<CancellationToken, Task> work,
        string module,
        string eventName,
        CancellationToken ct = default,
        bool toastOnError = false);
}
