using Greenlight.SampleClient;

namespace Greenlight.SampleClient.UnitTests;

/// <summary>
/// The traffic in the SDK sample. Worth testing despite being a desk toy: the jam is the
/// one part that looks perfect for ten seconds and then folds in on itself after a lap,
/// which is not something anyone is going to catch by watching a taskbar.
/// </summary>
public class TrafficSimulationTests
{
    private const double Width = 800;
    private const double CarHeight = 26;

    private static TrafficSimulation Sim(LightState light = LightState.Green, int cars = 5, double jamSeconds = 2, int seed = 1234)
    {
        var specs = Enumerable.Range(0, cars)
            .Select(i => new CarSpec((CarFlavor)(i % 5), "#4FB0FF", 0.8 + i * 0.1))
            .ToList();

        // Two seconds rather than five minutes: the behaviour is the same and the test run
        // is not.
        var sim = new TrafficSimulation(specs, seed: seed, jamArrivalInterval: TimeSpan.FromSeconds(jamSeconds));
        sim.Resize(Width, CarHeight);
        sim.Light = light;
        return sim;
    }

    /// <summary>
    /// Every car the simulation owns: on the road, plus the ones sitting out their wait
    /// between one lap and the next.
    /// </summary>
    private static IReadOnlyList<Car> Fleet(TrafficSimulation sim) => [.. sim.Cars, .. sim.Waiting];

    /// <summary>Run for a while at a steady frame rate, as the render loop does.</summary>
    private static void Run(TrafficSimulation sim, double seconds, double step = 1.0 / 60)
    {
        for (var t = 0.0; t < seconds; t += step)
            sim.Advance(TimeSpan.FromSeconds(step));
    }

    /// <summary>No car may ever be inside the car in front of it.</summary>
    private static void AssertNobodyOverlaps(TrafficSimulation sim)
    {
        var cars = sim.Cars;
        for (var i = 1; i < cars.Count; i++)
        {
            var ahead = cars[i - 1];
            var behind = cars[i];
            Assert.True(behind.X <= ahead.X - ahead.Length + 0.001,
                $"Car {i} (nose {behind.X:0.0}) is inside car {i - 1} (tail {ahead.X - ahead.Length:0.0}).");
        }
    }

    [Fact]
    public void On_green_the_cars_move()
    {
        var sim = Sim(LightState.Green);

        // Tracked by identity, not by index: the list rotates when a car wraps around, so
        // comparing position [i] before and after would be comparing two different cars —
        // and the front car can reach the end of an 800px strip inside a second.
        var tracked = sim.Cars[2];
        var before = tracked.X;

        Run(sim, 1.0);

        Assert.True(tracked.X > before, $"Car went from {before:0.0} to {tracked.X:0.0}.");
        Assert.All(sim.Cars, car => Assert.True(car.Speed > 0, "A car is stopped on a green light."));
    }

    [Fact]
    public void With_no_Greenlight_attached_nothing_moves()
    {
        // The disabled state, drawn. Parked cars say "nothing to report" where cars still
        // driving on a four-minute-old snapshot would be a lie.
        var sim = Sim(LightState.Unknown);
        var before = sim.Cars.Select(c => c.X).ToList();

        Run(sim, 2.0);

        Assert.Equal(before, sim.Cars.Select(c => c.X));
    }

    [Fact]
    public void On_red_the_front_car_stops_at_the_light()
    {
        var sim = Sim(LightState.Red);

        Run(sim, 20);

        var front = sim.Cars[0];
        Assert.True(front.X <= sim.StopLine + 0.001, $"Nose at {front.X:0.0} is past the line at {sim.StopLine:0.0}.");
        Assert.Equal(sim.StopLine, front.X, 1);
        Assert.Equal(0, front.Speed, 3);
    }

    [Fact]
    public void On_red_everyone_queues_up_behind_it()
    {
        var sim = Sim(LightState.Red);

        Run(sim, 30);

        AssertNobodyOverlaps(sim);
        Assert.All(sim.Cars, car => Assert.Equal(0, car.Speed, 2));

        // Bumper to bumper, not scattered across the strip: every car should be within a
        // couple of car lengths of the one in front once the queue has settled.
        for (var i = 1; i < sim.Cars.Count; i++)
        {
            var gap = sim.Cars[i - 1].X - sim.Cars[i - 1].Length - sim.Cars[i].X;
            Assert.InRange(gap, 0, CarHeight);
        }
    }

    [Fact]
    public void A_red_light_after_a_green_run_still_gathers_everyone_in()
    {
        // The order the cars are held in survives a lap, so a jam after some of them have
        // wrapped around has to work exactly as well as a jam from a standing start.
        var sim = Sim(LightState.Green);
        Run(sim, 25);

        sim.Light = LightState.Red;
        Run(sim, 40);

        AssertNobodyOverlaps(sim);
        Assert.Equal(sim.StopLine, sim.Cars[0].X, 1);
    }

    [Fact]
    public void Green_after_red_gets_everyone_going_again()
    {
        var sim = Sim(LightState.Red);
        Run(sim, 20);

        sim.Light = LightState.Green;
        Run(sim, 2);

        Assert.All(sim.Cars, car => Assert.True(car.Speed > 0, "A car is still stopped two seconds into a green light."));
    }

    [Fact]
    public void A_car_that_drives_off_the_end_comes_back_round_the_other_side()
    {
        var sim = Sim(LightState.Green);

        Run(sim, 40);

        AssertNobodyOverlaps(sim);
        Assert.All(sim.Cars, car => Assert.True(double.IsFinite(car.X)));
        // Everyone is somewhere sensible — nobody has been left a screen and a half away.
        Assert.All(sim.Cars, car => Assert.InRange(car.X, -Width, Width * 2));
    }

    [Fact]
    public void Nothing_overlaps_over_a_long_run_of_changing_lights()
    {
        // The one that would actually have caught a broken queue: four minutes of simulated
        // traffic through every transition, checked every frame.
        var sim = Sim(LightState.Green);
        var lights = new[] { LightState.Green, LightState.Yellow, LightState.Red, LightState.Green, LightState.Unknown, LightState.Red };

        foreach (var light in lights)
        {
            sim.Light = light;
            for (var t = 0.0; t < 40; t += 1.0 / 60)
            {
                sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
                AssertNobodyOverlaps(sim);
            }
        }
    }

    [Fact]
    public void Yellow_is_slower_than_green()
    {
        var green = Sim(LightState.Green);
        var yellow = Sim(LightState.Yellow);

        Run(green, 3);
        Run(yellow, 3);

        var greenSpeed = green.Cars.Sum(c => c.Speed);
        var yellowSpeed = yellow.Cars.Sum(c => c.Speed);

        Assert.True(yellowSpeed < greenSpeed, $"Yellow ({yellowSpeed:0}) is not slower than green ({greenSpeed:0}).");
        Assert.True(yellowSpeed > 0, "Yellow should crawl, not stop.");
    }

    [Fact]
    public void Only_a_yellow_light_gets_beeped_at()
    {
        // Counted across the run rather than sampled at the end: each beep lives about a
        // second and they are spaced a second or so apart, so "are there any right now"
        // is a coin toss even when it is working perfectly.
        var sim = Sim(LightState.Yellow);
        Assert.True(BeepsSeenOver(sim, 6) > 0, "A yellow light went unremarked upon.");

        // Green has nothing to complain about, and red is resignation.
        foreach (var quiet in new[] { LightState.Green, LightState.Red, LightState.Unknown })
        {
            sim.Light = quiet;

            // A beep already in the air when the light changed is allowed to finish fading
            // — it is a bubble with a life of its own, not a readout of the current light.
            // What must not happen is a new one after that.
            Run(sim, 1.5);
            Assert.Equal(0, BeepsSeenOver(sim, 4));
        }
    }

    /// <summary>The most beeps on screen at any point during the run.</summary>
    private static int BeepsSeenOver(TrafficSimulation sim, double seconds)
    {
        var most = 0;
        for (var t = 0.0; t < seconds; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            most = Math.Max(most, sim.Beeps.Count);
        }

        return most;
    }

    [Fact]
    public void Beeps_do_not_pile_up_or_double_up()
    {
        var sim = Sim(LightState.Yellow);

        for (var t = 0.0; t < 30; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            Assert.True(sim.Beeps.Count <= 3, $"{sim.Beeps.Count} beeps at once is a wall of text, not a toy.");
            Assert.Equal(sim.Beeps.Count, sim.Beeps.Select(b => b.Car).Distinct().Count());
        }
    }

    [Fact]
    public void A_frame_that_took_a_whole_second_does_not_teleport_anyone()
    {
        // A machine that goes away for a moment — a debugger break, a locked screen — must
        // not come back to cars scattered past the light.
        var sim = Sim(LightState.Red);
        Run(sim, 20);
        var front = sim.Cars[0].X;

        sim.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(front, sim.Cars[0].X, 1);
        AssertNobodyOverlaps(sim);
    }

    [Fact]
    public void Resizing_the_road_relays_it_out_without_a_pile_up()
    {
        var sim = Sim(LightState.Green);
        Run(sim, 10);

        sim.Resize(1920, 32);
        AssertNobodyOverlaps(sim);

        Run(sim, 10);
        AssertNobodyOverlaps(sim);
        Assert.Equal(1920, sim.Width);
    }

    // ── A jam that keeps growing ──────────────────────────────────────────────

    [Fact]
    public void A_red_light_left_alone_gathers_more_cars()
    {
        // A build broken all afternoon should look worse than a build broken at lunchtime.
        var sim = Sim(LightState.Red);
        Assert.Equal(5, sim.Cars.Count);

        Run(sim, 2.5);
        Assert.Equal(6, sim.Cars.Count);

        Run(sim, 2.0);
        Assert.Equal(7, sim.Cars.Count);
    }

    [Fact]
    public void Nobody_turns_up_while_the_light_is_green()
    {
        var sim = Sim(LightState.Green);

        Run(sim, 12);

        // Counted including the cars waiting their turn to come back on: laps take some of
        // them off the road, but the fleet is still five.
        Assert.Equal(5, Fleet(sim).Count);
    }

    [Theory]
    [InlineData(LightState.Yellow)]
    [InlineData(LightState.Unknown)]
    public void Only_a_red_light_gathers_a_crowd(LightState light)
    {
        var sim = Sim(light);

        Run(sim, 12);

        Assert.Equal(5, Fleet(sim).Count);
    }

    [Fact]
    public void The_five_minutes_is_not_restarted_by_a_flicker_through_yellow()
    {
        // Yellow means the build is still not fixed. Only a green light — the thing actually
        // being waited for — puts the clock back to zero.
        var sim = Sim(LightState.Red);
        Run(sim, 1.5);

        sim.Light = LightState.Yellow;
        Run(sim, 0.5);
        sim.Light = LightState.Red;
        Run(sim, 0.6);

        Assert.Equal(6, sim.Cars.Count);
    }

    [Fact]
    public void A_green_light_puts_the_clock_back_to_zero()
    {
        var sim = Sim(LightState.Red);
        Run(sim, 1.8);                      // nearly due

        sim.Light = LightState.Green;
        Run(sim, 0.5);
        sim.Light = LightState.Red;
        Run(sim, 0.5);                      // would have been due, had the clock survived

        Assert.Equal(5, sim.Cars.Count);
    }

    [Fact]
    public void The_road_fills_up_and_then_stops_filling()
    {
        var sim = Sim(LightState.Red, jamSeconds: 0.2);

        Run(sim, 120);
        var full = sim.Cars.Count;

        Run(sim, 60);

        // Past a full road the extra cars would stack up off screen where nobody can see
        // them — memory spent saying nothing that the full road has not already said.
        Assert.True(full > 5, $"The jam never grew past {full} cars.");
        Assert.Equal(full, sim.Cars.Count);
        AssertNobodyOverlaps(sim);
    }

    [Fact]
    public void A_full_road_is_full_of_road_and_not_of_cars_stacked_off_screen()
    {
        var sim = Sim(LightState.Red, jamSeconds: 0.2);

        Run(sim, 120);

        // Everything that arrived should end up somewhere a person can see it.
        var tail = sim.Cars[^1];
        Assert.True(tail.X - tail.Length > -CarHeight * 3,
            $"The back of the queue is at {tail.X - tail.Length:0.0}, well off the left edge.");
    }

    [Fact]
    public void When_the_light_goes_green_the_crowd_leaves_and_the_original_cars_stay()
    {
        var sim = Sim(LightState.Red, jamSeconds: 0.5);
        Run(sim, 6);
        Assert.True(sim.Cars.Count > 5, "The jam did not grow, so there is nothing to drain.");

        sim.Light = LightState.Green;
        Run(sim, 90);

        // Back to the cars that were configured — not merely back to five of something.
        Assert.Equal(5, Fleet(sim).Count);
        Assert.All(Fleet(sim), car => Assert.False(car.IsExtra));
        Assert.Equal(
            sim.Specs.Select(spec => spec.Color).ToHashSet(),
            Fleet(sim).Select(car => car.Spec.Color).ToHashSet());
    }

    [Fact]
    public void The_crowd_leaves_one_car_at_a_time_rather_than_vanishing()
    {
        // Culled as each one reaches the far end, not deleted the instant the light changes:
        // five cars disappearing off the middle of the road would look like a bug.
        var sim = Sim(LightState.Red, jamSeconds: 0.5);
        Run(sim, 6);
        var jammed = sim.Cars.Count;

        sim.Light = LightState.Green;

        var counts = new List<int>();
        for (var t = 0.0; t < 90; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            var total = Fleet(sim).Count;      // a fleet car off the road is still a fleet car
            if (counts.Count == 0 || counts[^1] != total) counts.Add(total);
        }

        // Strictly decreasing, one at a time, from the jam down to the fleet.
        Assert.Equal(Enumerable.Range(0, jammed - 4).Select(i => jammed - i), counts);
    }

    [Fact]
    public void Nothing_overlaps_while_a_jam_grows_and_drains()
    {
        var sim = Sim(LightState.Red, jamSeconds: 0.3);

        for (var t = 0.0; t < 60; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            AssertNobodyOverlaps(sim);
        }

        sim.Light = LightState.Green;
        for (var t = 0.0; t < 90; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            AssertNobodyOverlaps(sim);
        }
    }

    [Fact]
    public void The_cars_do_not_always_line_up_in_the_order_they_were_configured()
    {
        // Five cars laid out in config order every time would make the road a slideshow of
        // the same procession. A different seed has to give a different line-up.
        var specs = Enumerable.Range(0, 6)
            .Select(i => new CarSpec(CarFlavor.Sedan, $"#{i}{i}{i}{i}{i}{i}"))
            .ToList();

        var orders = Enumerable.Range(0, 12)
            .Select(seed =>
            {
                var sim = new TrafficSimulation(specs, seed: seed);
                sim.Resize(Width, CarHeight);
                return string.Join(",", sim.Cars.Select(c => c.Spec.Color));
            })
            .ToHashSet();

        Assert.True(orders.Count > 1, "Every layout came out in the same order.");
    }

    [Fact]
    public void A_car_coming_back_round_does_not_rejoin_the_same_procession()
    {
        // The line-up has to keep changing lap after lap, not just at the start.
        var sim = Sim(LightState.Green, cars: 6);

        var seen = new HashSet<string>();
        for (var t = 0.0; t < 120; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            seen.Add(string.Join(",", sim.Cars.Select(c => c.CruiseSpeed.ToString("0.0"))));
        }

        // Six cars cycling round unchanged can only ever show six rotations of one order.
        Assert.True(seen.Count > 6, $"Only {seen.Count} orders over two minutes of traffic.");
        AssertNobodyOverlaps(sim);
    }

    [Fact]
    public void Nothing_a_person_can_see_ever_jumps_sideways()
    {
        // Cars come and go from the waiting pen off screen; anything visible must only ever
        // move forwards, by no more than a frame's worth of its own speed.
        var sim = Sim(LightState.Green, cars: 6);
        var previous = sim.Cars.ToDictionary(c => c, c => c.X);

        for (var t = 0.0; t < 60; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));

            foreach (var car in sim.Cars)
            {
                // Visible cars only, and not the frame a car wraps around on.
                if (car.X - car.Length > 0 && previous.TryGetValue(car, out var before) && before > 0)
                    Assert.InRange(car.X - before, -0.001, car.CruiseSpeed / 60 + 0.001);
            }

            previous = sim.Cars.ToDictionary(c => c, c => c.X);
        }
    }

    [Fact]
    public void A_car_that_leaves_waits_a_while_before_it_comes_back()
    {
        // The fix for the fleet slowly collecting into one clump: the gap between laps is a
        // wait, not a distance, so two cars that left together do not return together.
        var sim = Sim(LightState.Green, cars: 5);

        var waitedAtAll = false;
        for (var t = 0.0; t < 60; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            waitedAtAll |= sim.Waiting.Count > 0;
            Assert.Equal(5, Fleet(sim).Count);
        }

        Assert.True(waitedAtAll, "No car ever came off the road, so nothing is waiting its turn.");
    }

    [Fact]
    public void A_car_never_comes_back_on_top_of_the_one_in_front()
    {
        // The wait is a floor, not a promise: a car whose turn comes up while the last one
        // is still half on screen has to keep waiting.
        var sim = Sim(LightState.Green, cars: 8);

        for (var t = 0.0; t < 120; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            AssertNobodyOverlaps(sim);
            Assert.All(sim.Cars, car => Assert.True(car.X <= sim.Width + car.Length + 1));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1234)]
    [InlineData(99999)]
    public void The_fleet_does_not_end_up_driving_around_as_one_clump(int seed)
    {
        // What this was all for. Cars cruise at different speeds on a single lane, so a
        // fast one catches a slow one and sits behind it; left to re-enter nose-to-tail
        // they end up as one block shuffling round the strip forever. Measured as the gap
        // between each car and the one in front, over minutes of traffic: bumper to bumper
        // is what a red light is supposed to look like, not a green one.
        var sim = Sim(LightState.Green, cars: 5, seed: seed);
        Run(sim, 45);                       // long enough for the fleet to have lapped a few times

        var gaps = new List<double>();
        for (var t = 0.0; t < 150; t += 1.0 / 60)
        {
            sim.Advance(TimeSpan.FromSeconds(1.0 / 60));
            for (var i = 1; i < sim.Cars.Count; i++)
                gaps.Add(sim.Cars[i - 1].X - sim.Cars[i - 1].Length - sim.Cars[i].X);
        }

        Assert.NotEmpty(gaps);
        var median = gaps.Order().ElementAt(gaps.Count / 2);
        Assert.True(median > Width * 0.1,
            $"Typical gap between cars was {median:0} on a {Width:0} strip — that is a convoy, not traffic.");
    }

    [Fact]
    public void A_red_light_still_gathers_the_cars_that_were_off_the_road()
    {
        // The spacing rule must not outlast the green light it is for: a red light is the
        // one time they are meant to pile up, and a fleet left waiting off screen would
        // empty the strip exactly when it should look full.
        var sim = Sim(LightState.Green);
        Run(sim, 30);
        Assert.NotEmpty(sim.Waiting);       // the run has to have left someone off the road

        sim.Light = LightState.Red;
        Run(sim, 60);

        // Counting the configured fleet, not the total: a minute of red light is also a
        // minute of the jam gathering a crowd, which is its own behaviour.
        Assert.Empty(sim.Waiting);
        Assert.Equal(5, sim.Cars.Count(car => !car.IsExtra));
        Assert.All(sim.Cars, car => Assert.Equal(0, car.Speed, 2));
        AssertNobodyOverlaps(sim);
    }

    [Fact]
    public void Nothing_rolls_back_on_while_there_is_no_Greenlight_attached()
    {
        // A parked road with one car quietly rolling in from the left would be the one
        // thing moving on a strip whose whole point is that it has nothing to say.
        var sim = Sim(LightState.Green);
        Run(sim, 20);

        sim.Light = LightState.Unknown;
        var onRoad = sim.Cars.Count;
        Run(sim, 30);

        Assert.Equal(onRoad, sim.Cars.Count);
    }

    [Fact]
    public void Every_flavour_has_its_own_length()
    {
        // Five silhouettes, and at taskbar height the length is most of what tells them
        // apart.
        var sim = new TrafficSimulation(
            Enum.GetValues<CarFlavor>().Select(f => new CarSpec(f, "#FFFFFF")).ToList(), seed: 7);
        sim.Resize(Width, CarHeight);

        var lengths = sim.Cars.Select(c => c.Length).ToList();

        Assert.Equal(5, lengths.Count);
        Assert.Equal(5, lengths.Distinct().Count());
        Assert.All(lengths, l => Assert.InRange(l, CarHeight, CarHeight * 3));
    }

    [Fact]
    public void Each_car_keeps_its_own_colour()
    {
        var specs = new List<CarSpec>
        {
            new(CarFlavor.Sedan, "#FF0000"),
            new(CarFlavor.Sedan, "#00FF00"),
            new(CarFlavor.Sedan, "#0000FF"),
        };

        var sim = new TrafficSimulation(specs, seed: 3);
        sim.Resize(Width, CarHeight);
        Run(sim, 20);

        // Same flavour, three colours. Compared as a set, because a lap around the strip
        // legitimately reorders the list — but it must never lose or duplicate a colour.
        Assert.Equal(
            new HashSet<string> { "#FF0000", "#00FF00", "#0000FF" },
            Fleet(sim).Select(c => c.Spec.Color).ToHashSet());
    }

    [Fact]
    public void A_configuration_with_no_cars_still_produces_traffic()
    {
        // A hand-edited config with an empty array should not give you an empty taskbar and
        // no clue why.
        var sim = new TrafficSimulation([], seed: 1);
        sim.Resize(Width, CarHeight);

        Assert.NotEmpty(sim.Cars);
    }
}
