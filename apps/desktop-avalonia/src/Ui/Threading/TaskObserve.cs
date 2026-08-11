using System;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Diagnostics;

namespace PacToolkits.Desktop.Avalonia.Ui.Threading;

/// <summary>观察未等待任务的异常，避免异常静默丢失</summary>
public static class TaskObserve
{
    public static void Observe(
        Task task,
        string module,
        string eventName,
        string message = "Detached task failed")
    {
        ArgumentNullException.ThrowIfNull(task);

        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        if (task.IsCanceled)
        {
            return;
        }

        if (task.IsFaulted)
        {
            LogFault(task, module, eventName, message);
            return;
        }

        _ = ObserveCoreAsync(task, module, eventName, message);
    }

    public static void Observe<T>(
        Task<T> task,
        string module,
        string eventName,
        string message = "Detached task failed")
        => Observe((Task)task, module, eventName, message);

    internal static Exception Unwrap(Exception ex)
        => ex switch
        {
            AggregateException { InnerExceptions.Count: 1 } aggregate => aggregate.InnerExceptions[0],
            AggregateException aggregate => aggregate.GetBaseException(),
            _ => ex
        };

    private static async Task ObserveCoreAsync(Task task, string module, string eventName, string message)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn(module, eventName, message, Unwrap(ex));
        }
    }

    private static void LogFault(Task task, string module, string eventName, string message)
    {
        if (task.Exception is null)
        {
            return;
        }

        foreach (var ex in task.Exception.InnerExceptions)
        {
            if (ex is OperationCanceledException)
            {
                continue;
            }

            AppLog.Warn(module, eventName, message, Unwrap(ex));
        }
    }
}
