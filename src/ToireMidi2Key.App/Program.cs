using Avalonia;
using System;
using System.IO;
using ToireMidi2Key;

namespace ToireMidi2Key.App;

sealed class Program
{
    // 是否自动提权由界面开关决定（config.json 的 autoElevate）；调试器附加时 Elevation 内部自动跳过，否则断点全废。
    [STAThread]
    public static void Main(string[] args)
    {
        if (ShouldAutoElevate(args) && Elevation.TryRelaunchAsAdmin(args)) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool ShouldAutoElevate(string[] args)
    {
        // --no-elevate：本次启动跳过自动提权（自动化测试、或临时用普通权限跑时很方便）
        if (args.Any(a => string.Equals(a, "--no-elevate", StringComparison.OrdinalIgnoreCase))) return false;

        try
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            return File.Exists(configPath) && ToireMidi2KeyConfig.Load(configPath).AutoElevate;
        }
        catch
        {
            return false;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
