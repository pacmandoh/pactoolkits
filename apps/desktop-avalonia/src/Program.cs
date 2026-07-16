using System;
using System.Globalization;
using Avalonia;
using Velopack;

namespace PacToolkits.Desktop.Avalonia;

internal sealed class Program
{
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.NotifyAsync().GetAwaiter().GetResult();
            return;
        }

        using (instance)
        {
            Instance = instance;
            instance.Listen();

            var zhCn = CultureInfo.GetCultureInfo("zh-CN");
            CultureInfo.DefaultThreadCurrentCulture = zhCn;
            CultureInfo.DefaultThreadCurrentUICulture = zhCn;
            CultureInfo.CurrentCulture = zhCn;
            CultureInfo.CurrentUICulture = zhCn;

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                Instance = null;
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
