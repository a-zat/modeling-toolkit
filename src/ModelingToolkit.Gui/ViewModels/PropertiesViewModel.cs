using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A row of the properties panel.</summary>
public sealed record PropertyRow(string Name, string Value);

/// <summary>The properties of the selected node of the model tree.</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    public PropertiesViewModel()
    {
        Title = string.Empty;
        Kind = string.Empty;
        Rows = [];
    }

    [ObservableProperty]
    public partial string Title { get; private set; }

    /// <summary>What the selection is: model, block reference or mesh.</summary>
    [ObservableProperty]
    public partial string Kind { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<PropertyRow> Rows { get; private set; }

    [ObservableProperty]
    public partial bool HasSelection { get; private set; }

    public void Show(ModelNodeViewModel? node)
    {
        HasSelection = node is not null;
        Title = node?.Name ?? string.Empty;
        Kind = node switch
        {
            null => string.Empty,
            { IsModel: true } => "Model",
            { IsReference: true } => "Block reference",
            { IsMesh: true } => "Mesh",
            _ => "Entity",
        };
        Rows = node switch
        {
            { Document: { } document } => ModelRows(document),
            { Entity: BlockReference reference } => ReferenceRows(reference),
            { Entity: MeshEntity mesh } => MeshRows(mesh),
            _ => [],
        };
    }

    private static List<PropertyRow> ModelRows(Document document)
    {
        DocumentStatistics statistics = DocumentStatistics.Of(document);
        return
        [
            new("Blocks", Formatting.Count(statistics.Blocks)),
            new("Block references", Formatting.Count(statistics.BlockReferences)),
            new("Meshes", $"{Formatting.Count(statistics.Meshes)} ({Formatting.Count(statistics.PlacedMeshes)} placed)"),
            new("Triangles", $"{Formatting.Count(statistics.Triangles)} ({Formatting.Count(statistics.PlacedTriangles)} placed)"),
            new("Materials", Formatting.Count(statistics.Materials)),
            new("Textures", Formatting.Count(statistics.Textures)),
            new("Size", Formatting.Size(statistics.BoundingBox)),
        ];
    }

    private static List<PropertyRow> ReferenceRows(BlockReference reference)
    {
        Transform transform = reference.Transform;
        return
        [
            new("Block", reference.Block.Name),
            new("Position", Formatting.Point(transform.M03, transform.M13, transform.M23)),
            new("Entities", Formatting.Count(reference.Block.Entities.Count)),
            new("Material", MaterialName(reference.Material)),
            new("Size", Formatting.Size(reference.GetBoundingBox())),
        ];
    }

    private static List<PropertyRow> MeshRows(MeshEntity mesh) =>
    [
        new("Vertices", Formatting.Count(mesh.Vertices.Length)),
        new("Triangles", Formatting.Count(mesh.Triangles.Length)),
        new("Normals", mesh.Normals is null ? "No" : "Yes"),
        new("Texture coordinates", mesh.TextureCoordinates is null ? "No" : "Yes"),
        new("Material", MaterialName(mesh.Material)),
        new("Size", Formatting.Size(mesh.GetBoundingBox())),
    ];

    private static string MaterialName(Material? material) => material?.Name ?? "By block";
}
