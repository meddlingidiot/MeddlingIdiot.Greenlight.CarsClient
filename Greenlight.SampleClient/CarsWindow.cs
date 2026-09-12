using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Greenlight.SampleClient;

/// <summary>
/// The road: a transparent, always-on-top, click-through strip laid over the taskbar, with
/// the traffic drawn on it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CarsWindow : Window
{
    private readonly CarsConfig _config;
    private readonly TrafficCanvas _canvas;
    private readonly DispatcherTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan _lastFrame;
    private TimeSpan _lastHousekeeping;
    private StripBounds _strip;

    public CarsWindow(CarsConfig config, TrafficSimulation simulation)
    {
        _config = config;
        Simulation = simulation;

        Title = "Greenlight cars";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Never take the caret out of somebody's editor. Paired with WS_EX_NOACTIVATE,
        // which is what makes a click landing here harmless in the first place.
        ShowActivated = false;

        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _canvas = new TrafficCanvas(simulation) { Opacity = config.Opacity };
        Content = _canvas;

        // 60fps. A desk toy that stutters is worse than no desk toy, and the whole frame is
        // a few dozen filled paths.
        _frames = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frames.Tick += OnFrame;
    }

    public TrafficSimulation Simulation { get; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Both of these need a real window handle, so neither can run before it is shown.
        ClickThroughNative.Apply(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        LayOutRoad();

        _lastFrame = _clock.Elapsed;
        _frames.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frames.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Take up a setting changed from the tray while the cars are running: how solid they
    /// are, how big, and where the road sits.
    /// </summary>
    /// <remarks>
    /// The config object is shared with the tray, so there is nothing to pass — this is the
    /// road being told to go and look again.
    /// </remarks>
    public void ApplyConfig()
    {
        _canvas.Opacity = _config.Opacity;
        LayOutRoad();
    }

    /// <summary>
    /// Put the window over the strip and tell the simulation how long the road is. Re-run
    /// when the taskbar moves or the screen changes size.
    /// </summary>
    public void LayOutRoad()
    {
        var strip = DesktopStrip.Find(_config.Placement, _config.FallbackStripHeight);
        if (strip.IsEmpty) return;

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        // Position is physical, Width and Height are logical. Mixing those up puts the road
        // at a plausible-looking but wrong size on every scaled display, which is most of
        // them.
        Position = new PixelPoint(strip.X, strip.Y);
        Width = strip.Width / scaling;
        Height = strip.Height / scaling;

        _strip = strip;
        Simulation.Resize(Width, Height * _config.CarScale);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastFrame;
        _lastFrame = now;

        // Once a second: re-claim the top of the z-order, and notice if the taskbar has
        // moved, resized or gone away. Cheap, and much less code than listening for every
        // way Windows has of mentioning either.
        if (now - _lastHousekeeping >= TimeSpan.FromSeconds(1))
        {
            _lastHousekeeping = now;

            ClickThroughNative.KeepOnTop(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

            if (DesktopStrip.Find(_config.Placement, _config.FallbackStripHeight) != _strip)
                LayOutRoad();
        }

        Simulation.Advance(elapsed);
        _canvas.InvalidateVisual();
    }
}
