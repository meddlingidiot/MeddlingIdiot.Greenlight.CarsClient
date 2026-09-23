using Automation.Velopack;
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

        // One per session, and the second one leaves without a word. Two copies — the one
        // Windows started and the one somebody launched by hand — would each react to the same
        // build and fight over the screen, which looks like a bug in the client rather than
        // like two of it.
        //
        // Taken after the Velopack hooks, so an install or an update running beside a copy that
        // is already up is never turned away. Local\ is this logon session, so two people signed
        // in to the same machine each get their own.
        using var single = new Mutex(initiallyOwned: true, @"Local\Greenlight.CarsClient", out var first);
        if (!first) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
