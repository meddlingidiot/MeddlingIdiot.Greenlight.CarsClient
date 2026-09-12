using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Greenlight.SampleClient;

/// <summary>
/// Draws the traffic. Everything is drawn from the simulation's numbers each frame — there
/// are no control instances per car, because five cars' worth of layout and hit testing
/// would be five cars' worth of work to no purpose on a window nobody can click.
/// </summary>
public sealed class TrafficCanvas : Control
{
    private static readonly IBrush WheelBrush = new SolidColorBrush(Color.FromRgb(24, 24, 28));
    private static readonly IBrush HubBrush = new SolidColorBrush(Color.FromRgb(120, 124, 132));
    private static readonly IBrush GlassBrush = new SolidColorBrush(Color.FromArgb(150, 214, 240, 255));
    private static readonly IBrush HeadlightBrush = new SolidColorBrush(Color.FromArgb(230, 255, 246, 200));
    private static readonly IBrush TaillightBrush = new SolidColorBrush(Color.FromArgb(230, 255, 90, 70));
    private static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));

    private static readonly IBrush BubbleBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
    private static readonly IBrush BubbleTextBrush = new SolidColorBrush(Color.FromRgb(28, 28, 32));
    private static readonly IPen BubblePen = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1);

    private static readonly IBrush SignalBodyBrush = new SolidColorBrush(Color.FromRgb(38, 40, 46));
    private static readonly IBrush LampOffBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

    private readonly Dictionary<string, IBrush> _bodyBrushes = [];

    public TrafficCanvas(TrafficSimulation simulation)
    {
        Simulation = simulation;
        IsHitTestVisible = false;
    }

    public TrafficSimulation Simulation { get; }

    public override void Render(DrawingContext context)
    {
        var height = Bounds.Height;
        if (height <= 1) return;

        // Parked cars are dimmed rather than hidden: an empty strip looks like the toy
        // crashed, where five faint cars looks like what it is — nothing to report.
        using var _ = context.PushOpacity(Simulation.Light == LightState.Unknown ? 0.38 : 1.0);

        DrawSignal(context, height);

        foreach (var car in Simulation.Cars)
            DrawCar(context, car, height);

        foreach (var beep in Simulation.Beeps)
            DrawBeep(context, beep, height);
    }

    // ── cars ──────────────────────────────────────────────────────────────────

    private void DrawCar(DrawingContext context, Car car, double stripHeight)
    {
        var h = Simulation.CarHeight;
        var length = car.Length;
        var right = car.X;
        var left = right - length;

        if (right < -length || left > Bounds.Width) return;

        // Sat on the floor of the strip with a little clearance, so the cars look like they
        // are on the taskbar rather than floating over it.
        var baseline = stripHeight - Math.Max(2, stripHeight * 0.10);
        var wheelRadius = h * 0.20;
        var bodyBottom = baseline - wheelRadius * 0.55;
        var bodyTop = bodyBottom - h * 0.42;
        var roofTop = bodyBottom - h;

        context.DrawEllipse(ShadowBrush, null,
            new Point(left + length / 2, baseline + wheelRadius * 0.35),
            length * 0.46, wheelRadius * 0.42);

        var body = Body(car.Spec.Color);

        // Cabin first, so the lower body's rounded corners sit over its base edge.
        context.DrawGeometry(body, null, Cabin(car.Spec.Flavor, left, right, bodyTop, roofTop, h));
        context.DrawGeometry(GlassBrush, null, Glass(car.Spec.Flavor, left, right, bodyTop, roofTop, h));

        context.DrawRectangle(body, null,
            new RoundedRect(new Rect(left, bodyTop, length, bodyBottom - bodyTop), h * 0.18));

        // Lights. Front is on the right — everything drives left to right.
        context.DrawRectangle(HeadlightBrush, null,
            new RoundedRect(new Rect(right - h * 0.14, bodyTop + h * 0.10, h * 0.12, h * 0.12), h * 0.05));
        context.DrawRectangle(TaillightBrush, null,
            new RoundedRect(new Rect(left + h * 0.03, bodyTop + h * 0.10, h * 0.10, h * 0.12), h * 0.05));

        DrawWheel(context, left + length * 0.24, baseline - wheelRadius * 0.1, wheelRadius);
        DrawWheel(context, left + length * 0.78, baseline - wheelRadius * 0.1, wheelRadius);
    }

    private static void DrawWheel(DrawingContext context, double x, double y, double radius)
    {
        context.DrawEllipse(WheelBrush, null, new Point(x, y), radius, radius);
        context.DrawEllipse(HubBrush, null, new Point(x, y), radius * 0.38, radius * 0.38);
    }

    /// <summary>
    /// The part above the waistline, which is the only thing that distinguishes one flavour
    /// from another at this size — at 25 pixels tall a wheelbase is not a silhouette.
    /// </summary>
    private static Geometry Cabin(CarFlavor flavor, double left, double right, double bodyTop, double roofTop, double h)
    {
        var length = right - left;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        switch (flavor)
        {
            case CarFlavor.Van:
                // Tall and square, roof running nearly the whole length.
                ctx.BeginFigure(new Point(left + length * 0.05, bodyTop), true);
                ctx.LineTo(new Point(left + length * 0.05, roofTop));
                ctx.LineTo(new Point(right - length * 0.12, roofTop));
                ctx.LineTo(new Point(right - length * 0.02, bodyTop));
                break;

            case CarFlavor.Pickup:
                // Cabin over the front axle, flat bed behind it.
                ctx.BeginFigure(new Point(left + length * 0.42, bodyTop), true);
                ctx.LineTo(new Point(left + length * 0.50, roofTop + h * 0.06));
                ctx.LineTo(new Point(right - length * 0.16, roofTop + h * 0.06));
                ctx.LineTo(new Point(right - length * 0.06, bodyTop));
                break;

            case CarFlavor.SportsCar:
                // One low wedge, long bonnet, no upright anything.
                ctx.BeginFigure(new Point(left + length * 0.14, bodyTop), true);
                ctx.LineTo(new Point(left + length * 0.36, roofTop + h * 0.26));
                ctx.LineTo(new Point(left + length * 0.64, roofTop + h * 0.26));
                ctx.LineTo(new Point(right - length * 0.10, bodyTop));
                break;

            case CarFlavor.Hatchback:
                // Short, with the back of the roof falling straight to the tail.
                ctx.BeginFigure(new Point(left + length * 0.06, bodyTop), true);
                ctx.LineTo(new Point(left + length * 0.20, roofTop + h * 0.08));
                ctx.LineTo(new Point(right - length * 0.26, roofTop + h * 0.08));
                ctx.LineTo(new Point(right - length * 0.10, bodyTop));
                break;

            default:
                // Sedan: cabin in the middle, a boot behind and a bonnet in front.
                ctx.BeginFigure(new Point(left + length * 0.22, bodyTop), true);
                ctx.LineTo(new Point(left + length * 0.34, roofTop + h * 0.10));
                ctx.LineTo(new Point(right - length * 0.32, roofTop + h * 0.10));
                ctx.LineTo(new Point(right - length * 0.20, bodyTop));
                break;
        }

        ctx.EndFigure(true);
        return geometry;
    }

    /// <summary>The glass, inset inside the cabin so the body colour still frames it.</summary>
    private static Geometry Glass(CarFlavor flavor, double left, double right, double bodyTop, double roofTop, double h)
    {
        var inset = (right - left) * 0.055;
        var cabin = Cabin(flavor, left + inset, right - inset, bodyTop - h * 0.04, roofTop + h * 0.07, h);
        return cabin;
    }

    private IBrush Body(string color)
    {
        // Parsed once per colour, not once per frame: at 60fps a Color.Parse per car is
        // pure waste, and a bad hex in a hand-edited config should degrade to a visible car
        // rather than an exception sixty times a second.
        if (_bodyBrushes.TryGetValue(color, out var brush)) return brush;

        try
        {
            brush = new SolidColorBrush(Color.Parse(color));
        }
        catch
        {
            brush = new SolidColorBrush(Color.FromRgb(150, 150, 158));
        }

        _bodyBrushes[color] = brush;
        return brush;
    }

    // ── the light at the end of the road ──────────────────────────────────────

    private void DrawSignal(DrawingContext context, double stripHeight)
    {
        var h = Simulation.CarHeight;
        var lampSize = h * 0.26;
        var bodyWidth = lampSize * 1.9;
        var bodyHeight = lampSize * 5.2;

        var x = Simulation.StopLine + h * 0.24;
        var bottom = stripHeight - Math.Max(2, stripHeight * 0.10);
        var top = Math.Max(1, bottom - bodyHeight);

        if (x > Bounds.Width) return;

        context.DrawRectangle(SignalBodyBrush, null,
            new RoundedRect(new Rect(x, top, bodyWidth, bottom - top), lampSize * 0.5));

        var spacing = (bottom - top) / 3.4;
        var cx = x + bodyWidth / 2;

        Lamp(0, LightState.Red, Color.FromRgb(240, 70, 60));
        Lamp(1, LightState.Yellow, Color.FromRgb(245, 190, 70));
        Lamp(2, LightState.Green, Color.FromRgb(80, 210, 130));

        void Lamp(int index, LightState state, Color lit)
        {
            var cy = top + spacing * (index + 0.7);
            var on = Simulation.Light == state;

            if (on)
            {
                // A soft halo, so the live lamp reads as lit rather than merely coloured.
                context.DrawEllipse(new SolidColorBrush(lit, 0.28), null, new Point(cx, cy), lampSize, lampSize);
            }

            context.DrawEllipse(on ? new SolidColorBrush(lit) : LampOffBrush, null,
                new Point(cx, cy), lampSize * 0.5, lampSize * 0.5);
        }
    }

    // ── BEEP BEEP ─────────────────────────────────────────────────────────────

    private void DrawBeep(DrawingContext context, Beep beep, double stripHeight)
    {
        var h = Simulation.CarHeight;
        var fade = Math.Clamp(beep.Remaining / beep.Life, 0, 1);

        // Rises and fades as it goes, which is what makes three of them at once read as
        // three separate complaints rather than as clutter.
        var rise = (1 - fade) * h * 0.55;

        var text = new FormattedText(
            "BEEP BEEP",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            Math.Max(7, h * 0.30),
            BubbleTextBrush);

        var padX = h * 0.16;
        var padY = h * 0.08;
        var width = text.Width + padX * 2;
        var height = text.Height + padY * 2;

        var centreX = beep.Car.X - beep.Car.Length / 2;
        var x = centreX - width / 2;
        var y = stripHeight - h * 1.05 - height - rise;

        // Nudged inside the strip rather than clipped: half a speech bubble off the edge of
        // the screen looks like a bug, not like a car leaving.
        x = Math.Clamp(x, 2, Math.Max(2, Bounds.Width - width - 2));
        if (y < 1) y = 1;

        using var _ = context.PushOpacity(fade);

        var bubble = new RoundedRect(new Rect(x, y, width, height), height * 0.42);
        context.DrawRectangle(BubbleBrush, BubblePen, bubble);

        // The tail, pointing back down at whoever is doing the beeping.
        var tail = new StreamGeometry();
        using (var tctx = tail.Open())
        {
            var tipX = Math.Clamp(centreX, x + height * 0.4, x + width - height * 0.4);
            tctx.BeginFigure(new Point(tipX - height * 0.18, y + height - 1), true);
            tctx.LineTo(new Point(tipX + height * 0.18, y + height - 1));
            tctx.LineTo(new Point(tipX, y + height + height * 0.34));
            tctx.EndFigure(true);
        }

        context.DrawGeometry(BubbleBrush, null, tail);
        context.DrawText(text, new Point(x + padX, y + padY));
    }
}
