using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A material of the library: what a new material is made from when chosen.</summary>
/// <param name="Color">As it is seen, in sRGB, "#RRGGBB".</param>
/// <param name="Opacity">From 0, invisible, to 1, opaque.</param>
public sealed record LibraryMaterial(string Name, string Category, string Color, float Metallic, float Roughness, float Opacity = 1f)
{
    /// <summary>A new material with the values of this one.</summary>
    public Material Create() => new(Name)
    {
        BaseColor = ColorSpaces.ToLinear(Color, Opacity),
        Metallic = Metallic,
        Roughness = Roughness,
    };

    /// <summary>Whether a material has the values of this one, whatever its name, as one made from it has.</summary>
    public bool Matches(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.BaseColorTexture is null && material.BaseColor == Create().BaseColor &&
            material.Metallic == Metallic && material.Roughness == Roughness;
    }
}

/// <summary>The materials that the application offers, by category: colours of our own, without textures.</summary>
public static class MaterialLibrary
{
    public static IReadOnlyList<string> Categories { get; } = ["Matte", "Glossy", "Metal", "Glass", "Wood", "Stone"];

    public static IReadOnlyList<LibraryMaterial> Materials { get; } =
    [
        Matte("White", "#F2F2EF"), Matte("Light grey", "#C9CCD1"), Matte("Grey", "#8F949C"),
        Matte("Graphite", "#3A3D42"), Matte("Black", "#1C1D1F"), Matte("Sand", "#D8C7A3"), Matte("Red", "#B8352D"),
        Matte("Orange", "#D9772B"), Matte("Ochre", "#D8A33C"), Matte("Green", "#4E8A5B"), Matte("Blue", "#3D6FB6"),
        Matte("Navy", "#23395E"),

        Glossy("White plastic", "#F4F4F2"), Glossy("Black plastic", "#202124"), Glossy("Red plastic", "#C8322B"),
        Glossy("Yellow plastic", "#E8B521"), Glossy("Green plastic", "#3C9A52"), Glossy("Blue plastic", "#2F64C8"),

        Metal("Steel", "#A8ADB3", 0.4f), Metal("Brushed steel", "#9DA3AA", 0.55f), Metal("Aluminium", "#D0D3D6", 0.35f),
        Metal("Chrome", "#E6E8EA", 0.08f), Metal("Cast iron", "#55585C", 0.7f), Metal("Brass", "#C9A24D", 0.3f),
        Metal("Copper", "#C47A52", 0.3f), Metal("Gold", "#D4AF37", 0.25f),

        new("Clear glass", "Glass", "#E8F0F2", 0f, 0.05f, 0.25f),
        new("Tinted glass", "Glass", "#6E8C8F", 0f, 0.05f, 0.45f),
        new("Frosted glass", "Glass", "#DDE5E8", 0f, 0.5f, 0.55f),

        Wood("Oak", "#C39A6B"), Wood("Walnut", "#6B4A33"), Wood("Pine", "#E0C08F"), Wood("Cherry", "#9A4E35"),
        Wood("Birch", "#E6D3AE"), Wood("Ebony", "#2E2520"),

        Stone("Concrete", "#A3A39E"), Stone("Granite", "#7D7B78"), Stone("Marble", "#ECEAE5", 0.3f),
        Stone("Slate", "#4F555C"), Stone("Brick", "#9C4A36"), Stone("Sandstone", "#CDB28A"),
    ];

    public static IEnumerable<LibraryMaterial> In(string category) =>
        Materials.Where(material => material.Category == category);

    private static LibraryMaterial Matte(string name, string color) => new(name, "Matte", color, 0f, 0.9f);

    private static LibraryMaterial Glossy(string name, string color) => new(name, "Glossy", color, 0f, 0.25f);

    private static LibraryMaterial Metal(string name, string color, float roughness) =>
        new(name, "Metal", color, 1f, roughness);

    private static LibraryMaterial Wood(string name, string color) => new(name, "Wood", color, 0f, 0.75f);

    private static LibraryMaterial Stone(string name, string color, float roughness = 0.85f) =>
        new(name, "Stone", color, 0f, roughness);
}

/// <summary>Colours as they are seen, sRGB, and as materials hold them, linear.</summary>
internal static class ColorSpaces
{
    /// <summary>A colour "#RRGGBB" as the linear base colour of a material.</summary>
    public static Color ToLinear(string hex, float opacity = 1f)
    {
        Avalonia.Media.Color parsed = Avalonia.Media.Color.Parse(hex);
        return ToLinear(parsed.R, parsed.G, parsed.B, opacity);
    }

    public static Color ToLinear(byte r, byte g, byte b, float opacity = 1f) =>
        new(Linear(r / 255f), Linear(g / 255f), Linear(b / 255f), opacity);

    /// <summary>The colour of a material as it is seen, for a swatch.</summary>
    public static Avalonia.Media.Color ToSrgb(Color color) => Avalonia.Media.Color.FromArgb(
        (byte)Math.Round(Math.Clamp(color.A, 0f, 1f) * 255), Byte(color.R), Byte(color.G), Byte(color.B));

    private static float Linear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static byte Byte(float linear)
    {
        float value = Math.Clamp(linear, 0f, 1f);
        float srgb = value <= 0.0031308f ? 12.92f * value : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;
        return (byte)Math.Round(srgb * 255f);
    }
}
