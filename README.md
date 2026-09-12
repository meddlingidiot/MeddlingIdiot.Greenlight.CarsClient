# MeddlingIdiot.Greenlight.CarsClient

Little cars that drive along your taskbar and pile up at a red light when a pipeline breaks.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK, and a demonstration of how little an app needs to do to use it. The traffic flows while
everything is green, crawls and honks **BEEP BEEP** on yellow, and jams at the light on red —
and the longer a build stays broken, the more cars join the back of the queue, until the road
is full. Fix the build and they drive off one at a time, leaving the fleet you configured.
With no Greenlight running at all, the cars park: a desk toy that kept driving on a
four-minute-old snapshot would be lying to you.

The point of it is what it does **not** have. No Azure DevOps client, no GitHub client, no
token, no polling loop — everything it knows arrives through the SDK, from the Greenlight
already running on the machine. Strip out the drawing and the tray icon and the integration
is about twenty lines, all of them in [`App.cs`](Greenlight.SampleClient/App.cs).

## Running it

```bash
dotnet run --project Greenlight.SampleClient
```

Windows only: the click-through strip and the taskbar-finding are Win32. The SDK itself is
not — it is plain .NET, and the same twenty lines work anywhere.

## The tray

The cars ignore the mouse on purpose — a window you cannot click is a window that never steals
your caret, and also one you cannot turn off. So everything lives on the mascot in the
notification area:

- **Cars on the road** — stop and start them. Clicking the icon does the same.
- **Where the road goes** — on top of the taskbar, across it, or along the bottom of the screen.
- **Car size**, **How solid** — how much of the strip they take up, and how much they show through.
- **Traffic builds up after** — how long a red light runs before one more car joins the queue.
- **Edit the cars…** — opens `cars.json`. **Reload the file** picks up hand edits without a restart.

Every setting is written straight back to the file, so the menu and the JSON are never two
different sets of settings.

## The cars themselves

`%AppData%\Greenlight.SampleCars\cars.json`, written out with the defaults on first run. Five
shapes — hatchback, sedan, van, pickup, sports car — each its own colour and its own cruise
speed. Five is the default, not the limit.

```json
{
  "Cars": [
    { "Flavor": "Hatchback", "Color": "#4FB0FF", "SpeedFactor": 1.0 },
    { "Flavor": "SportsCar", "Color": "#F2574C", "SpeedFactor": 1.35 }
  ],
  "Placement": "AboveTaskbar",
  "JamArrivalMinutes": 5
}
```

A file that cannot be parsed falls back to the defaults rather than refusing to start. It is a
desk toy; a stray comma should not cost you the whole thing.

## Building on it

```bash
dotnet add package MeddlingIdiot.Greenlight.Sdk
```

That is exactly what this repository does — the SDK comes from nuget.org like any other
dependency. (`Automation.Velopack/` is still carried here: the updater is not published yet,
and it is not something a client of your own needs.)

Greenlight itself is a separate product and is not open source — this sample is. The two
things it wants you to notice: the client sits in a disabled state and reconnects on its own
when Greenlight is not running, so there is nothing to guard; and the events arrive on a
background thread, so anything touching your UI has to get itself back onto the UI thread. Both
are worked through in `App.cs`.

## Tests

```bash
dotnet test
```

The traffic simulation is deliberately free of Avalonia so it can be tested without a window.
That is not ceremony: a queue that folds in on itself after a lap, or a fleet that slowly
collects into one clump, is not something anyone is going to catch by watching a taskbar.

## Licence

MIT — see [LICENSE](LICENSE). The MeddlingIdiot name and mascot are not part of that grant;
see [TRADEMARKS.md](TRADEMARKS.md).
