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
    public DisplayMode DisplayMode { get; init; } = DisplayMode.ShadedWithEdges;

    public bool Perspective { get; init; } = true;

    public bool Grid { get; init; } = true;

    /// <summary>Whether the 3D view tints what is under the pointer.</summary>
    public bool HoverHighlight { get; init; }

    /// <summary>The hour of the day the rendered display mode shows, from 0 to 24.</summary>
    public double TimeOfDay { get; init; } = 12;

    /// <summary>Where the model is, in degrees north, for the course of the sun.</summary>
    public double Latitude { get; init; } = 45.5;

    /// <summary>The direction of north from the Y axis of the model, in degrees counterclockwise.</summary>
    public double NorthAngle { get; init; }

    /// <summary>The day of the sun in the rendered display mode; null for today.</summary>
    public DateOnly? SunDate { get; init; }

    /// <summary>The cover of clouds in the rendered display mode, from 0, a clear sky, to 1.</summary>
    public double Cloudiness { get; init; } = 0.35;

    /// <summary>The colour of the ground in the rendered display mode, as "#RRGGBB".</summary>
    public string GroundColor { get; init; } = "#5C6B4D";

    /// <summary>The width, in points, of the edges drawn over the faces.</summary>
    public double EdgeWidth { get; init; } = 1.0;

    /// <summary>The width, in points, of the outlines of the CAD render style.</summary>
    public double OutlineWidth { get; init; } = 1.5;

    /// <summary>Whether walking stops at walls and other parts of the model.</summary>
    public bool Collisions { get; init; } = true;

    /// <summary>Whether, while walking, yellow dots mark what can be used.</summary>
    public bool ShowUsable { get; init; }

    /// <summary>Whether walking follows the ground: it falls off an edge and rises over a low step.</summary>
    public bool Gravity { get; init; } = true;

    /// <summary>How near the eyes come to what stops the walk, in millimetres.</summary>
    public double CollisionRadius { get; init; } = 250;

    /// <summary>Of the eyes above the floor when walking, in millimetres.</summary>
    public double EyeHeight { get; init; } = 1600;

    /// <summary>The speed of a walk, in millimetres per second.</summary>
    public double WalkSpeed { get; init; } = 1400;

    /// <summary>How much faster running is than walking.</summary>
    public double RunFactor { get; init; } = 2.5;

    /// <summary>The vertical field of view when walking, in degrees.</summary>
    public double WalkFieldOfView { get; init; } = 65;

    /// <summary>The turn of a drag when walking, in degrees per point of the screen.</summary>
    public double LookSensitivity { get; init; } = 0.23;
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
