using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Greenlight.CarsClient;

/// <summary>
/// The mascot in the notification area, and the menu hanging off him: the only part of this
/// toy a person can actually click.
/// </summary>
/// <remarks>
/// <para>
/// The cars themselves are click-through by design — a window you cannot hit is a window
/// you cannot turn off, which left "edit the JSON and restart it" as the only way to change
/// anything. Hence a tray icon: every setting in <see cref="CarsConfig"/> that can be
/// changed while the thing is running is reachable from here, and each change is written
/// straight back to the file, so the menu and the JSON are always the same settings.
/// </para>
/// <para>
/// Avalonia's own <see cref="TrayIcon"/> rather than a tray library, because the sample is
/// meant to be readable — and because a sample that drags in a dependency to draw one icon
/// is making a point nobody asked for.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CarsTray : IDisposable
{
    private static readonly Uri IconUri = new("avares://Greenlight.CarsClient/Assets/MeddlingIdiot.ico");

    private readonly CarsConfig _config;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _status;
    private readonly NativeMenuItem _running;
    private readonly NativeMenuItem _startup;

    public CarsTray(CarsConfig config)
    {
        _config = config;

        _status = new NativeMenuItem { Header = "Waiting for Greenlight…", IsEnabled = false };
        _running = new NativeMenuItem
        {
            Header = "Cars on the road",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = true,
        };
        _running.Click += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);

        // Read from the registry rather than from a setting of ours, every time it is shown: the
        // user can turn this off in Task Manager's Startup tab, and a tick remembering what we
        // last wrote would then be telling them the opposite of the truth.
        _startup = Check("Start with Windows", WindowsStartup.IsEnabled, value => WindowsStartup.Set(value));

        var menu = BuildMenu();

        // The submenus re-tick their own items when they open. This one is top-level, and it is
        // the only top-level item that can change behind our back — so this is the moment to notice.
        menu.Opening += (_, _) => _startup.IsChecked = WindowsStartup.IsEnabled();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(IconUri)),
            ToolTipText = "Greenlight cars",
            Menu = menu,
            IsVisible = true,
        };

        // The one thing a left click can mean here. There is no main window to open, and a
        // tray icon that does nothing at all when clicked reads as a hung one.
        _tray.Clicked += (_, _) => SetRunning(!IsRunning?.Invoke() ?? true);
    }

    /// <summary>Whether the cars are currently on the road.</summary>
    public Func<bool>? IsRunning { get; set; }

    /// <summary>Put the cars on the road, or take them off it.</summary>
    public Action<bool>? OnSetRunning { get; set; }

    /// <summary>
    /// A setting changed that the running road can absorb — where it sits, how big the cars
    /// are, how solid they look.
    /// </summary>
    public Action? OnConfigChanged { get; set; }

    /// <summary>
    /// A setting changed that the running road cannot absorb, so the traffic has to be laid
    /// out again from scratch: the fleet itself, or how fast a jam gathers.
    /// </summary>
    public Action? OnRestartTraffic { get; set; }

    /// <summary>Re-read the config file, for edits made by hand while this was running.</summary>
    public Action? OnReloadConfig { get; set; }

    public Action? OnQuit { get; set; }

    /// <summary>Say what the light is doing, in the tooltip and at the top of the menu.</summary>
    public void ShowLight(LightState light)
    {
        var running = IsRunning?.Invoke() ?? true;

        _status.Header = light switch
        {
            LightState.Green => "Greenlight: green — traffic flowing",
            LightState.Yellow => "Greenlight: yellow — traffic crawling",
            LightState.Red => "Greenlight: red — everything piling up",
            _ => "Greenlight not running — cars parked",
        };

        _running.IsChecked = running;
        _tray.ToolTipText = running ? $"Greenlight cars — {Short(light)}" : "Greenlight cars — stopped";
    }

    private static string Short(LightState light) => light switch
    {
        LightState.Green => "green",
        LightState.Yellow => "yellow",
        LightState.Red => "red",
        _ => "not connected",
    };

    public void Dispose()
    {
        _tray.IsVisible = false;
        _tray.Dispose();
    }

    private void SetRunning(bool running)
    {
        OnSetRunning?.Invoke(running);
        _running.IsChecked = running;
        _tray.ToolTipText = running ? "Greenlight cars" : "Greenlight cars — stopped";
    }

    private NativeMenu BuildMenu() =>
    [
        _status,
        new NativeMenuItemSeparator(),
        _running,
        Submenu("Where the road goes",
            Choice("On top of the taskbar", () => _config.Placement == StripPlacement.AboveTaskbar,
                () => SetPlacement(StripPlacement.AboveTaskbar)),
            Choice("Across the taskbar", () => _config.Placement == StripPlacement.OverTaskbar,
                () => SetPlacement(StripPlacement.OverTaskbar)),
            Choice("Bottom of the screen", () => _config.Placement == StripPlacement.BottomOfScreen,
                () => SetPlacement(StripPlacement.BottomOfScreen))),
        Submenu("Car size",
            Scale("Small", 0.45),
            Scale("Medium", 0.62),
            Scale("Large", 0.80)),
        Submenu("How solid",
            Opacity("Solid", 1.0),
            Opacity("Nearly solid", 0.8),
            Opacity("Half there", 0.5),
            Opacity("Barely there", 0.3)),
        Submenu("Traffic builds up after",
            Jam("Never", 0),
            Jam("1 minute", 1),
            Jam("5 minutes", 5),
            Jam("15 minutes", 15),
            Jam("An hour", 60)),
        new NativeMenuItemSeparator(),
        Item("Edit the cars…", EditConfig),
        Item("Reload the file", () => OnReloadConfig?.Invoke()),
        _startup,
        new NativeMenuItemSeparator(),
        Item("Quit", () => OnQuit?.Invoke()),
    ];

    // ── Menu plumbing ─────────────────────────────────────────────────────────
    // Each option asks the config what it should look like when the menu opens rather than
    // being ticked once at startup: the file is editable by hand and reloadable from this
    // very menu, so anything remembering its own state would start lying the moment it was.

    private static NativeMenuItem Check(string header, Func<bool> isOn, Action<bool> set)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isOn(),
        };

        item.Click += (_, _) =>
        {
            set(!isOn());
            item.IsChecked = isOn();
        };

        return item;
    }

    private static NativeMenuItem Item(string header, Action click)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }

    private static NativeMenuItem Submenu(string header, params NativeMenuItem[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items) menu.Add(item);

        void Retick()
        {
            foreach (var item in items)
                if (item.CommandParameter is Func<bool> isChosen)
                    item.IsChecked = isChosen();
        }

        // Twice, because neither moment is reliable on its own: picking an option has to
        // move the tick off the old one straight away, and opening the menu has to account
        // for the file having been edited by hand behind its back.
        foreach (var item in items) item.Click += (_, _) => Retick();
        menu.Opening += (_, _) => Retick();

        return new NativeMenuItem { Header = header, Menu = menu };
    }

    private static NativeMenuItem Choice(string header, Func<bool> isChosen, Action choose)
    {
        var item = new NativeMenuItem
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChosen(),

            // Parked here rather than in a dictionary: the menu owns its items, and a
            // second collection to keep in step with it is a second thing to get wrong.
            CommandParameter = isChosen,
        };

        item.Click += (_, _) => choose();
        return item;
    }

    private NativeMenuItem Scale(string header, double scale) =>
        Choice(header, () => Math.Abs(_config.CarScale - scale) < 0.001, () =>
        {
            _config.CarScale = scale;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Opacity(string header, double opacity) =>
        Choice(header, () => Math.Abs(_config.Opacity - opacity) < 0.001, () =>
        {
            _config.Opacity = opacity;
            Persist();
            OnConfigChanged?.Invoke();
        });

    private NativeMenuItem Jam(string header, double minutes) =>
        Choice(header, () => Math.Abs(_config.JamArrivalMinutes - minutes) < 0.001, () =>
        {
            _config.JamArrivalMinutes = minutes;
            Persist();

            // The interval is fixed when the simulation is built, so this one cannot be
            // absorbed by the road as it stands.
            OnRestartTraffic?.Invoke();
        });

    private void SetPlacement(StripPlacement placement)
    {
        _config.Placement = placement;
        Persist();
        OnConfigChanged?.Invoke();
    }

    private void Persist() => _config.Save();

    /// <summary>
    /// Open <c>cars.json</c> in whatever the machine opens JSON with. The colours and the
    /// fleet are too open-ended to put in a menu — five cars is the default, not the limit —
    /// so the menu's job there is just to make the file findable.
    /// </summary>
    private void EditConfig()
    {
        try
        {
            // It is written out on first run, but a deleted file should still open something
            // rather than nothing.
            if (!File.Exists(CarsConfig.DefaultPath)) _config.Save();

            Process.Start(new ProcessStartInfo(CarsConfig.DefaultPath) { UseShellExecute = true });
        }
        catch
        {
            // No editor associated with .json, or the shell refused. A desk toy does not get
            // to interrupt anyone over it.
        }
    }
}
