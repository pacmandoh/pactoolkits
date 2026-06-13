using System;
using Avalonia;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class AppLog
{
    public static IAppLogger? TryGetLogger()
    {
        try
        {
            if (global::Avalonia.Application.Current is App app)
                return app.Services.GetService(typeof(IAppLogger)) as IAppLogger;
        }
        catch
        {
            // ignored
        }

        return null;
    }

    public static void Info(string module, string eventName, string message, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
            return;

        logger.Info(module, eventName, message, context);
    }

    public static void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
            return;

        logger.Warn(module, eventName, message, ex, context);
    }

    public static void Error(string module, string eventName, string message, Exception? ex = null, object? context = null)
    {
        var logger = TryGetLogger();
        if (logger is null)
            return;

        logger.Error(module, eventName, message, ex, context);
    }
}
