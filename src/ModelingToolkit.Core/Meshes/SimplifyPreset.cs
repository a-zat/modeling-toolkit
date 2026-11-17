namespace ModelingToolkit.Core.Meshes;

/// <summary>A named set of the settings of the simplification.</summary>
public sealed record SimplifyPreset(string Name, string Description, SimplifyOptions Options)
{
    public static IReadOnlyList<SimplifyPreset> All { get; } =
    [
        new("Clean up", "Welds the vertices and takes out broken triangles; the shape stays as it is.",
            new SimplifyOptions()),
        new("Fewer lines", "Softens the creases of the file under 60°: fewer edges drawn, the geometry as it is.",
            new SimplifyOptions { SoftenAngle = 60 }),
        new("Furniture at room scale", "Takes out small parts, smooths lightly, reduces the triangles within 6 mm, softens creases.",
            new SimplifyOptions
            {
                MinPieceSize = 15, SmoothIterations = 5, SmoothMaxMove = 3, KeepPercent = 50, MaxError = 6,
                SoftenAngle = 50,
            }),
        new("Background object", "Strong: fewer triangles within 15 mm, no small parts, softer shading, one mesh per material.",
            new SimplifyOptions
            {
                MinPieceSize = 50, SmoothIterations = 10, SmoothMaxMove = 10, KeepPercent = 20, MaxError = 15,
                SoftenAngle = 60, MergeByMaterial = true,
            }),
    ];

    /// <summary>The preset of a name, whatever its case and spaces; null for none.</summary>
    public static SimplifyPreset? Named(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string wanted = Plain(name);
        return All.FirstOrDefault(preset => Plain(preset.Name) == wanted);
    }

    private static string Plain(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    public override string ToString() => Name;
}
