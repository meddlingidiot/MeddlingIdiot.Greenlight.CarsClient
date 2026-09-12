using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.SampleClient;

/// <summary>
/// The cars, read from a JSON file the user can edit. Written out with the defaults the
/// first time it is missing, so "where do I change the colours" has an answer that does not
/// involve rebuilding anything.
/// </summary>
/// <remarks>
/// Kept in AppData rather than beside the executable: the executable lives under <c>bin</c>,
/// which a rebuild is entitled to delete, and losing someone's colours to a rebuild would be
/// its own small betrayal.
/// </remarks>
public sealed class CarsConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.SampleCars", "cars.json");

    /// <summary>
    /// One entry per car on the road. Five to begin with, one of each flavour, each its own
    /// colour — add, remove or repaint as you like; nothing cares how many there are.
    /// </summary>
    public List<CarSpec> Cars { get; set; } =
    [
        new(CarFlavor.Hatchback, "#4FB0FF", 1.00),
        new(CarFlavor.Sedan, "#F2C14E", 0.92),
        new(CarFlavor.Van, "#B07CE8", 0.80),
        new(CarFlavor.Pickup, "#5FD08A", 0.88),
        new(CarFlavor.SportsCar, "#F2574C", 1.35),
    ];

    /// <summary>
    /// Where the road goes. <see cref="StripPlacement.AboveTaskbar"/> rests it on the
    /// taskbar's top edge, so the cars never cover a taskbar button;
    /// <see cref="StripPlacement.OverTaskbar"/> drives them across the taskbar itself.
    /// Either way, a taskbar that is hidden or pinned to the left, right or top is ignored
    /// and the cars use the bottom of the desktop.
    /// </summary>
    public StripPlacement Placement { get; set; } = StripPlacement.AboveTaskbar;

    /// <summary>
    /// How tall the strip is, in physical pixels, when the taskbar cannot be used — it is
    /// hidden, or living down one side of the screen.
    /// </summary>
    public int FallbackStripHeight { get; set; } = 48;

    /// <summary>
    /// How long the light stays red before one more car joins the back of the queue, in
    /// minutes. The road keeps filling until it is full, and empties back to the configured
    /// cars once the light goes green. Zero or less turns the whole behaviour off.
    /// </summary>
    public double JamArrivalMinutes { get; set; } = 5;

    /// <summary>How much of the strip's height a car takes up. The rest is road.</summary>
    public double CarScale { get; set; } = 0.62;

    /// <summary>Overall opacity, for when the cars are livelier than you want them to be.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Load the file, writing the defaults out first if it is not there. A file that cannot
    /// be read or parsed falls back to the defaults rather than refusing to start: this is a
    /// desk toy, and a stray comma should not cost you the whole thing.
    /// </summary>
    public static CarsConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new CarsConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<CarsConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new CarsConfig();

            if (loaded.Cars.Count == 0) loaded.Cars = new CarsConfig().Cars;
            loaded.CarScale = Math.Clamp(loaded.CarScale, 0.2, 0.95);
            loaded.JamArrivalMinutes = Math.Clamp(loaded.JamArrivalMinutes, 0, 24 * 60);
            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.1, 1.0);
            loaded.FallbackStripHeight = Math.Clamp(loaded.FallbackStripHeight, 24, 200);
            return loaded;
        }
        catch
        {
            return new CarsConfig();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // A toy that cannot write its config still runs perfectly well on the defaults.
        }
    }
}
