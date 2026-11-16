using System.Text.Json.Serialization;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// The settings of the application, as kept in <c>settings.json</c>: changing them there, while the application runs,
/// is the same as changing them in its interface, which writes them there.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Whether files are opened and saved as glTF (.glb) directly, rather than in the native format.</summary>
    public bool UseGlb { get; init; } = true;

    public bool DarkTheme { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<RenderStyle>))]
    public RenderStyle RenderStyle { get; init; } = RenderStyle.Standard;

    [JsonConverter(typeof(JsonStringEnumConverter<DisplayMode>))]
    public DisplayMode DisplayMode { get; init; } = DisplayMode.Shaded;

    public bool Perspective { get; init; } = true;

    public bool Grid { get; init; } = true;

    /// <summary>The hour of the day the rendered display mode shows, from 0 to 24.</summary>
    public double TimeOfDay { get; init; } = 12;

    /// <summary>Where the model is, in degrees north, for the course of the sun.</summary>
    public double Latitude { get; init; } = 45.5;

    /// <summary>The direction of north from the Y axis of the model, in degrees counterclockwise.</summary>
    public double NorthAngle { get; init; }
}

/// <summary>Where the settings are kept.</summary>
public interface ISettingsStore
{
    /// <summary>The file holding them, for the user to edit; null when they are kept nowhere.</summary>
    string? Location { get; }

    AppSettings Load();

    void Save(AppSettings settings);

    /// <summary>Raised, on any thread, when the settings were changed outside the application.</summary>
    event EventHandler? Changed;
}

/// <summary>Settings kept in memory only, as in tests.</summary>
public sealed class MemorySettingsStore(AppSettings? settings = null) : ISettingsStore
{
    private AppSettings _settings = settings ?? new AppSettings();

    public string? Location => null;

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings) => _settings = settings;

    public event EventHandler? Changed { add { } remove { } }
}
