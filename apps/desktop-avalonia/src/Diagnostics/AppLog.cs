using System;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Diagnostics;

/// <summary>向当前应用日志服务转发无需实例依赖的诊断事件</summary>
public static class AppLog
{
    public static IAppLogger? TryGetLogger()
    {
        try
        {
            if (global::Avalonia.Application.Current is App app)
            {
                return app.Services.GetService(typeof(IAppLogger)) as IAppLogger;
            }
        }
        catch
        {
            // 日志服务不可用时保持诊断调用不影响业务流程
        }

        return null;
    }

    public static void Info(string module, string eventName, string message, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
        {
            return;
        }

        logger.Info(module, eventName, message, context, LogTrace.Current);
    }

    public static void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
        {
            return;
        }

        logger.Warn(module, eventName, message, ex, context, LogTrace.Current);
    }

    public static void Error(string module, string eventName, string message, Exception? ex = null, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
        {
            return;
        }

        logger.Error(module, eventName, message, ex, context, LogTrace.Current);
    }
}
