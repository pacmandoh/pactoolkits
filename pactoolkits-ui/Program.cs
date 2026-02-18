using Avalonia;
using System;
using System.Globalization;
using Velopack;

namespace pactoolkits_ui;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var zhCn = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = zhCn;
        CultureInfo.DefaultThreadCurrentUICulture = zhCn;
        CultureInfo.CurrentCulture = zhCn;
        CultureInfo.CurrentUICulture = zhCn;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
