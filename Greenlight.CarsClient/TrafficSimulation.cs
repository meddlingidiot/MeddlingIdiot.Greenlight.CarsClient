namespace Greenlight.CarsClient;

/// <summary>What the traffic is doing, which is Greenlight's aggregate colour by another name.</summary>
public enum LightState
{
    /// <summary>No Greenlight attached. The cars park rather than pretending to know anything.</summary>
    Unknown,

    /// <summary>Everything passing. Traffic flows.</summary>
    Green,

    /// <summary>Something pending. Traffic crawls, and gets impatient about it.</summary>
    Yellow,

    /// <summary>A pipeline is broken. Everything piles up at the light.</summary>
    Red,
}

/// <summary>The five shapes of car. Silhouette only — colour is per car, not per flavour.</summary>
public enum CarFlavor
{
    Hatchback,
    Sedan,
    Van,
    Pickup,
    SportsCar,
}

/// <summary>One car as configured: what it looks like, what colour it is, how keen it is.</summary>
/// <param name="Color">Any hex Avalonia can parse — <c>#E2574C</c>, <c>#CCE2574C</c>.</param>
/// <param name="SpeedFactor">Multiplier on the cruise speed. 1.0 is ordinary.</param>
public sealed record CarSpec(CarFlavor Flavor, string Color, double SpeedFactor = 1.0);

/// <summary>A car in motion. Positions are in logical pixels along the strip.</summary>
public sealed class Car
{
    public required CarSpec Spec { get; init; }

    /// <summary>Where its nose is. Cars drive left to right, so the tail is at <c>X - Length</c>.</summary>
    public double X { get; set; }

    public double Length { get; set; }

    public double Speed { get; set; }

    /// <summary>Its own cruise speed, so a line of cars does not move like one rigid object.</summary>
    public double CruiseSpeed { get; set; }

    /// <summary>
    /// Seconds left off the road before this car is allowed back on at the left-hand edge.
    /// Only meaningful while it is in <see cref="TrafficSimulation.Waiting"/>.
    /// </summary>
    public double WaitRemaining { get; set; }

    /// <summary>
    /// True for a car that joined the back of a jam rather than being one of the configured
    /// fleet. Extras are culled as they leave the far end once the light goes green, which is
    /// what returns the road to the cars the user actually asked for.
    /// </summary>
    public bool IsExtra { get; init; }
}

/// <summary>A "BEEP BEEP" over one car, fading out.</summary>
public sealed class Beep
{
    public required Car Car { get; init; }
    public double Remaining { get; set; }
    public double Life { get; init; }
}

/// <summary>
/// The traffic itself: where every car is, how fast, and what happens when the light
/// changes. Deliberately free of Avalonia — it is all arithmetic, so it can be tested
/// without a window, which is the only way the jam behaviour was ever going to be
/// checkable.
/// </summary>
public sealed class TrafficSimulation
{
    /// <summary>Logical pixels per second at a <see cref="CarSpec.SpeedFactor"/> of 1.</summary>
    private const double BaseCruiseSpeed = 115;

    private const double Acceleration = 220;

    /// <summary>Braking is stronger than acceleration, which is both true and what stops a queue overlapping.</summary>
    private const double Braking = 520;

    /// <summary>Bumper-to-bumper gap, as a fraction of car height. Queues look wrong with none.</summary>
    private const double GapFactor = 0.35;

    /// <summary>
    /// The shortest and longest a car waits off the road between leaving at one end and
    /// coming back on at the other.
    /// </summary>
    /// <remarks>
    /// A wait in seconds rather than a gap in pixels, which is the whole point of it: cars
    /// cruise at different speeds, so a fixed distance behind the last one put a fast car
    /// back on the road a fixed distance behind a slow one, and the fleet gradually
    /// collected into a single clump. Waiting for a while instead means two cars that left
    /// together do not come back together.
    /// </remarks>
    private const double MinReentryWait = 0.7;

    private const double MaxReentryWait = 4.5;

    /// <summary>
    /// How far down the road the last car has to be, as a fraction of the strip, before the
    /// next one is allowed on.
    /// </summary>
    /// <remarks>
    /// The wait on its own is not enough. Cars queue in the pen while the road is busy, and
    /// the moment the tail of the last one cleared the edge they would all pour on at once
    /// — a convoy rebuilt by the very thing meant to break it up. Making each arrival wait
    /// for real road in front of it is what actually keeps them apart.
    /// </remarks>
    /// <remarks>
    /// Not applied on red: a red light is the one time the cars are meant to gather, and
    /// holding them off the road to keep them apart would empty the strip at exactly the
    /// moment it is supposed to look full.
    /// </remarks>
    private const double EntryHeadwayFactor = 0.30;

    private static readonly IReadOnlyDictionary<CarFlavor, double> LengthRatios = new Dictionary<CarFlavor, double>
    {
        [CarFlavor.Hatchback] = 1.85,
        [CarFlavor.Sedan] = 2.20,
        [CarFlavor.Van] = 2.35,
        [CarFlavor.Pickup] = 2.45,
        [CarFlavor.SportsCar] = 2.15,
    };

    private readonly Random _random;
    private readonly List<Beep> _beeps = [];

    // Ordered front-most first: index 0 is the car nearest the light. Kept ordered rather
    // than re-sorted, because a car that wraps around has the largest X and the smallest
    // claim to be at the front — sorting would put it at the head of the queue and the whole
    // line would concertina into it.
    private readonly List<Car> _cars = [];

    // Off the road entirely: gone from the right-hand end, not yet back on at the left.
    // Kept out of _cars so it is out of the queue arithmetic and out of the renderer, both
    // of which are only ever meant to know about traffic that is actually on the strip.
    private readonly List<Car> _waiting = [];

    private double _nextBeepIn;

    /// <summary>How long the light has been red, for the every-so-often arrival of another car.</summary>
    private double _redFor;

    /// <param name="jamArrivalInterval">
    /// How long a red light runs before another car joins the back of the queue. Five minutes
    /// by default; tests pass something they are willing to wait for.
    /// </param>
    public TrafficSimulation(IReadOnlyList<CarSpec> specs, int? seed = null, TimeSpan? jamArrivalInterval = null)
    {
        _random = seed is { } s ? new Random(s) : new Random();
        Specs = specs.Count > 0 ? specs : [new CarSpec(CarFlavor.Hatchback, "#8CC8FF")];
        JamArrivalInterval = jamArrivalInterval is { TotalSeconds: > 0 } i ? i : TimeSpan.FromMinutes(5);
        Resize(800, 40);
    }

    /// <summary>How long a red light runs before one more car turns up behind the queue.</summary>
    public TimeSpan JamArrivalInterval { get; }

    public IReadOnlyList<CarSpec> Specs { get; }

    /// <summary>Front-most first. What the renderer walks.</summary>
    public IReadOnlyList<Car> Cars => _cars;

    /// <summary>
    /// Cars sitting out their wait between one lap and the next. Not drawn, and not part of
    /// the queue — but still part of the fleet, so a count of the configured cars is
    /// <see cref="Cars"/> and these together.
    /// </summary>
    public IReadOnlyList<Car> Waiting => _waiting;

    public IReadOnlyList<Beep> Beeps => _beeps;

    public LightState Light { get; set; } = LightState.Unknown;

    public double Width { get; private set; }

    public double CarHeight { get; private set; }

    /// <summary>Where a stopped car's nose comes to rest. Just short of the signal at the end.</summary>
    public double StopLine => Width - CarHeight * 0.9;

    /// <summary>
    /// Lay the road out again for a new strip size, keeping the cars spaced out behind the
    /// light rather than dropped in at random — a fresh start should look like traffic, not
    /// like a collision. Which car is in front is drawn fresh each time, so the fleet does
    /// not always arrive in the order the config file happens to list it in.
    /// </summary>
    public void Resize(double width, double carHeight)
    {
        Width = Math.Max(120, width);
        CarHeight = Math.Max(8, carHeight);

        _cars.Clear();
        _waiting.Clear();
        _beeps.Clear();
        _redFor = 0;

        var gap = CarHeight * GapFactor;
        var x = StopLine;

        foreach (var spec in Shuffled(Specs))
        {
            var length = CarHeight * LengthRatios[spec.Flavor];
            _cars.Add(new Car
            {
                Spec = spec,
                Length = length,
                X = x,
                Speed = 0,
                CruiseSpeed = BaseCruiseSpeed * Math.Clamp(spec.SpeedFactor, 0.2, 4.0),
            });

            // Spread them out over the strip so they arrive at the light in a line rather
            // than as one block.
            x -= length + gap + Width / Math.Max(2, Specs.Count) * 0.7;
        }
    }

    /// <summary>Move everything on by <paramref name="elapsed"/>.</summary>
    public void Advance(TimeSpan elapsed)
    {
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.1);   // a stalled frame must not teleport anyone
        if (dt <= 0) return;

        MoveCars(dt);
        Recycle();
        LetWaitingCarsBackOn(dt);
        UpdateBeeps(dt);
        UpdateJamArrivals(dt);
    }

    private void MoveCars(double dt)
    {
        var gap = CarHeight * GapFactor;

        for (var i = 0; i < _cars.Count; i++)
        {
            var car = _cars[i];

            // How far this car's nose is allowed to get: the light if it is holding, and
            // never past the tail of the car in front. The second rule applies whatever the
            // light is doing, which is what makes a queue rather than a pile.
            var limit = i == 0 ? FrontLimit() : _cars[i - 1].X - _cars[i - 1].Length - gap;
            var room = limit - car.X;

            if (room <= 0)
            {
                // Already at (or nudged past) the limit — sit on it. Being exactly stopped
                // matters: a queue that creeps is a queue that eventually overlaps.
                car.X = Math.Min(car.X, limit);
                car.Speed = 0;
                continue;
            }

            var cruise = Light == LightState.Yellow ? car.CruiseSpeed * 0.55 : car.CruiseSpeed;
            if (Light == LightState.Unknown) cruise = 0;

            // The fastest this car could be going and still stop in the room it has.
            var target = double.IsPositiveInfinity(room)
                ? cruise
                : Math.Min(cruise, Math.Sqrt(2 * Braking * room));

            car.Speed = car.Speed < target
                ? Math.Min(target, car.Speed + Acceleration * dt)
                : Math.Max(target, car.Speed - Braking * dt);

            car.X = Math.Min(limit, car.X + car.Speed * dt);
        }
    }

    /// <summary>Where the front car may go: the light holds it on red, nothing holds it otherwise.</summary>
    private double FrontLimit() => Light == LightState.Red ? StopLine : double.PositiveInfinity;

    /// <summary>
    /// A car whose tail has left the right-hand edge comes off the road and waits its turn
    /// to come back on at the other end.
    /// </summary>
    private void Recycle()
    {
        if (_cars.Count == 0) return;

        var front = _cars[0];
        if (front.X - front.Length <= Width) return;

        // A car that only turned up because the build was broken does not come back round.
        // Dropping it here rather than on the light turning green is what makes the road
        // empty out the way it filled: one car at a time, at the far end, as the jam clears.
        if (front.IsExtra)
        {
            _cars.RemoveAt(0);
            return;
        }

        _cars.RemoveAt(0);
        _beeps.RemoveAll(b => b.Car == front);      // its complaint left the road with it

        front.Speed = 0;
        front.WaitRemaining = MinReentryWait + _random.NextDouble() * (MaxReentryWait - MinReentryWait);
        _waiting.Add(front);
    }

    /// <summary>
    /// Put cars that have sat out their wait back on at the left-hand edge, nose first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A car only comes on once the road behind the current tail is clear, so the wait is a
    /// floor and not a promise — a car whose turn comes up while the last one is still
    /// half on screen keeps waiting. That check, rather than any amount of spacing
    /// arithmetic, is what guarantees a car never arrives on top of one already there.
    /// </para>
    /// <para>
    /// Which of several due cars goes first is drawn at random, so the fleet does not
    /// merely queue up off screen in the order it left.
    /// </para>
    /// <para>
    /// Nothing is due while the light is unknown: the road is parked, so a car rolling on
    /// from the left would be the one thing moving on a strip whose whole point at that
    /// moment is that it has nothing to say.
    /// </para>
    /// </remarks>
    private void LetWaitingCarsBackOn(double dt)
    {
        if (_waiting.Count == 0 || Light == LightState.Unknown) return;

        foreach (var car in _waiting) car.WaitRemaining -= dt;

        var gap = CarHeight * GapFactor;

        while (true)
        {
            // The nose starts on the edge, so the body is still off screen; what matters is
            // whether the last car on the road has left enough of it free.
            var tail = _cars.Count > 0 ? _cars[^1] : null;
            var headway = Light == LightState.Red ? gap : Math.Max(gap, Width * EntryHeadwayFactor);
            if (tail is not null && tail.X - tail.Length < headway) return;

            var due = _waiting.Where(c => c.WaitRemaining <= 0).ToList();
            if (due.Count == 0) return;

            var car = due[_random.Next(due.Count)];
            _waiting.Remove(car);

            car.X = 0;
            car.WaitRemaining = 0;

            // Already at speed: it has been driving all along, just not where anyone could
            // see it. Rolling on from a standstill would look like it stalled off screen.
            car.Speed = Light == LightState.Yellow ? car.CruiseSpeed * 0.55 : car.CruiseSpeed;
            _cars.Add(car);
        }
    }

    /// <summary>A copy in a random order. The caller's list is left alone.</summary>
    private IReadOnlyList<T> Shuffled<T>(IReadOnlyList<T> items)
    {
        var copy = items.ToArray();
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    private void UpdateBeeps(double dt)
    {
        for (var i = _beeps.Count - 1; i >= 0; i--)
        {
            _beeps[i].Remaining -= dt;
            if (_beeps[i].Remaining <= 0) _beeps.RemoveAt(i);
        }

        if (Light != LightState.Yellow)
        {
            // Only impatience beeps. A red light is resignation, and a green one has nothing
            // to complain about.
            _nextBeepIn = 0.4;
            return;
        }

        _nextBeepIn -= dt;
        if (_nextBeepIn > 0 || _beeps.Count >= 3) return;

        _nextBeepIn = 0.5 + _random.NextDouble();

        // Only from a car that is actually on screen — a beep from off the left edge is a
        // bubble floating over nothing.
        var visible = _cars.Where(c => c.X > 0 && c.X - c.Length < Width).ToList();
        if (visible.Count == 0) return;

        var car = visible[_random.Next(visible.Count)];
        if (_beeps.Any(b => b.Car == car)) return;

        _beeps.Add(new Beep { Car = car, Life = 1.15, Remaining = 1.15 });
    }

    /// <summary>
    /// While the light is red, another car joins the back of the queue every
    /// <see cref="JamArrivalInterval"/> — so a build left broken all afternoon is a road
    /// that is visibly, increasingly full, rather than the same five cars it was at lunch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Arrivals stop once the settled queue would reach the left-hand edge. Past that point
    /// the extra cars would be stacked off screen where nobody can see them, which costs
    /// memory and says nothing — the road being full is already the strongest thing this can
    /// say, and it should not keep saying it louder.
    /// </para>
    /// <para>
    /// The clock runs on red only, and is reset by green rather than by yellow: yellow means
    /// the build is still not fixed, and a flickering red/yellow should not keep restarting
    /// the five minutes.
    /// </para>
    /// </remarks>
    private void UpdateJamArrivals(double dt)
    {
        if (Light == LightState.Green)
        {
            _redFor = 0;
            return;
        }

        if (Light != LightState.Red) return;

        var interval = JamArrivalInterval.TotalSeconds;
        _redFor += dt;

        while (_redFor >= interval)
        {
            if (!RoomForAnother())
            {
                // Held at the threshold rather than left to grow: whenever the road does
                // have room again, the next car is due immediately and not thirty of them.
                _redFor = interval;
                return;
            }

            _redFor -= interval;
            AddCarToBackOfQueue();
        }
    }

    /// <summary>Whether the queue, once settled, would still fit on the visible road.</summary>
    private bool RoomForAnother()
    {
        var gap = CarHeight * GapFactor;

        // Measured from the lengths rather than from where the cars happen to be: while a
        // jam is still forming the tail is off screen anyway, and a position-based answer
        // would say "full" before a single extra had arrived.
        //
        // Cars waiting their turn off the road count too. They are coming back — a jam that
        // ignored them would keep inviting more until the road overflowed the moment they
        // did.
        var waiting = _cars.Count + _waiting.Count;
        var settled = _cars.Sum(c => c.Length) + _waiting.Sum(c => c.Length) + gap * Math.Max(0, waiting - 1);
        var next = CarHeight * LengthRatios[NextExtraSpec().Flavor] + gap;

        return settled + next <= StopLine;
    }

    /// <summary>
    /// What the next arrival looks like: the configured fleet, cycled through. A jam of
    /// strangers would be odd — these are meant to read as more of the same traffic.
    /// </summary>
    private CarSpec NextExtraSpec() => Specs[_cars.Count % Specs.Count];

    private void AddCarToBackOfQueue()
    {
        var spec = NextExtraSpec();
        var length = CarHeight * LengthRatios[spec.Flavor];
        var gap = CarHeight * GapFactor;

        var tail = _cars.Count > 0 ? _cars[^1] : null;
        var x = tail is null
            ? StopLine
            : Math.Min(-length, tail.X - tail.Length - gap);

        _cars.Add(new Car
        {
            Spec = spec,
            Length = length,
            X = x,
            Speed = 0,
            CruiseSpeed = BaseCruiseSpeed * Math.Clamp(spec.SpeedFactor, 0.2, 4.0),
            IsExtra = true,
        });
    }
}
