using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.SampleClient;

/// <summary>
/// The whole of the Greenlight integration, which is the point of the sample: attach,
/// translate the colour, and never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// The tray icon and the start/stop plumbing around it are ordinary Avalonia and have
/// nothing to do with Greenlight — the integration is still the twenty-odd lines in
/// <see cref="StartWatchingGreenlight"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    private GreenlightClient? _greenlight;
    private CarsWindow? _window;
    private CarsTray? _tray;
    private CarsConfig _config = new();

    /// <summary>
    /// The last thing Greenlight said. Held here rather than only in the simulation because
    /// the simulation comes and goes — stopped, restarted, rebuilt for a new fleet — and a
    /// road that came back showing green after a restart would be the toy lying.
    /// </summary>
    private LightState _light = LightState.Unknown;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The cars window is closed and reopened by the tray's stop/start, and there is
            // no other window — on the default setting, stopping the cars would quit the
            // whole thing and take the tray icon with it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = CarsConfig.Load();

            _tray = new CarsTray(_config)
            {
                IsRunning = () => _window is not null,
                OnSetRunning = running =>
                {
                    if (running) StartTraffic();
                    else StopTraffic();
                },
                OnConfigChanged = () => _window?.ApplyConfig(),
                OnRestartTraffic = RestartTraffic,
                OnReloadConfig = ReloadConfig,
                OnQuit = () => desktop.Shutdown(),
            };

            StartTraffic();
            StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both of these arrive on a background thread — the SDK says so, loudly, and this
        // is what it means in practice. Touching _window.Simulation from the pipe's thread
        // would be a race against the render loop reading the same cars.
        _greenlight.Changed += (_, e) => Apply(Translate(e.Snapshot.Status));
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            // Anything other than Connected means we have nothing to show, and saying so
            // by parking the cars is more honest than leaving them driving on stale data.
            if (e.Availability != GreenlightAvailability.Connected) Apply(LightState.Unknown);
        };

        // Deliberately not awaited and deliberately not guarded: StartAsync returns as soon
        // as the background loop is running, and an absent Greenlight is not an error. The
        // cars sit parked until one turns up, then start moving on their own.
        _ = _greenlight.StartAsync();
    }

    private void StartTraffic()
    {
        if (_window is not null) return;

        // A jam interval of zero means "never" — TrafficSimulation reads a non-positive
        // interval as its default, so the off switch has to be an interval nothing reaches.
        var arrivals = _config.JamArrivalMinutes > 0
            ? TimeSpan.FromMinutes(_config.JamArrivalMinutes)
            : TimeSpan.MaxValue;

        _window = new CarsWindow(_config, new TrafficSimulation(_config.Cars, jamArrivalInterval: arrivals))
        {
            Simulation = { Light = _light },
        };

        _window.Show();
        _tray?.ShowLight(_light);
    }

    private void StopTraffic()
    {
        _window?.Close();
        _window = null;
        _tray?.ShowLight(_light);
    }

    /// <summary>
    /// Lay the traffic out again from scratch, for the settings the running road cannot
    /// absorb: the fleet itself, and how long a red light runs before it gathers a crowd.
    /// </summary>
    private void RestartTraffic()
    {
        if (_window is null) return;    // stopped on purpose; it will pick the change up when it starts

        StopTraffic();
        StartTraffic();
    }

    /// <summary>Re-read the file, for colours and cars edited by hand while this was running.</summary>
    private void ReloadConfig()
    {
        var reloaded = CarsConfig.Load();

        // Copied into the existing instance rather than swapped for it: the tray is holding
        // the old one, and it is the tray's menu that has to keep agreeing with the file.
        _config.Cars = reloaded.Cars;
        _config.Placement = reloaded.Placement;
        _config.FallbackStripHeight = reloaded.FallbackStripHeight;
        _config.JamArrivalMinutes = reloaded.JamArrivalMinutes;
        _config.CarScale = reloaded.CarScale;
        _config.Opacity = reloaded.Opacity;

        RestartTraffic();
    }

    private static LightState Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => LightState.Green,
        GreenlightStatus.Yellow => LightState.Yellow,
        GreenlightStatus.Red => LightState.Red,
        _ => LightState.Unknown,
    };

    private void Apply(LightState light) =>
        Dispatcher.UIThread.Post(() =>
        {
            _light = light;
            if (_window is not null) _window.Simulation.Light = light;
            _tray?.ShowLight(light);
        });
}
