using MeddlingIdiot.Velopack;
using Avalonia;
using Velopack;

namespace Greenlight.CarsClient;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Before Avalonia, and before anything else. Velopack's hooks — first run, update,
        // uninstall — are handled inside Run(), which then exits the process; started any
        // later and an install would flash a window on its way past, or miss the hook
        // altogether. VelopackBootstrapper says as much itself, and it is the other half of
        // the reason this lives in Main: `args` is simply here.
        VelopackApp.Build().Run();
        VelopackBootstrapper.Startup("Greenlight.CarsClient", args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
