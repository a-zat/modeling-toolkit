using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A row of the properties panel.</summary>
public sealed record PropertyRow(string Name, string Value);

/// <summary>A material that an entity can take, or "By block" for none.</summary>
public sealed record MaterialChoice(string Name, Material? Material)
{
    public static MaterialChoice ByBlock { get; } = new("By block", null);
}

/// <summary>The properties of the selected node of the model tree, or of the faces, edges and vertices selected.</summary>
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

    /// <summary>Runs the commands that the panel makes, as the edits of its fields; null when nothing can be edited.</summary>
    public Func<IEditCommand, bool>? Edit { get; set; }

    /// <summary>Whether the selection is an entity, whose name and material can be changed.</summary>
    [ObservableProperty]
    public partial bool IsEditable { get; private set; }

    /// <summary>The name of the entity, as typed: setting it renames the entity, or takes its name away when empty.</summary>
    public string EditedName
    {
        get => _node?.Entity?.Name ?? string.Empty;
        set
        {
            string? name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_node?.Entity is { } entity && name != entity.Name)
            {
                Edit?.Invoke(new RenameCommand(entity, name));
            }
            OnPropertyChanged(); // the name the entity has, if the edit was refused or trimmed
        }
    }

    /// <summary>The materials of the document, after "By block".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MaterialChoice> MaterialChoices { get; private set; } = [];

    /// <summary>The material of the entity: choosing another one changes it.</summary>
    public MaterialChoice? SelectedMaterial
    {
        get => _node?.Entity is { } entity ? MaterialChoices.FirstOrDefault(c => ReferenceEquals(c.Material, entity.Material)) : null;
        set
        {
            // The list clears its selection when its items change: that is not a choice.
            if (value is not null && _node?.Entity is { } entity && !ReferenceEquals(value.Material, entity.Material))
            {
                Edit?.Invoke(new SetMaterialCommand(entity, value.Material));
            }
            OnPropertyChanged();
        }
    }

    private ModelNodeViewModel? _node;
    private Document? _document;
    private (Document Document, DocumentStatistics Statistics)? _known;

    /// <summary>
    /// Gives the statistics of a document, counted when it was read, as they take long on large models: the model node
    /// of that document shows them without counting again.
    /// </summary>
    public void Know(Document document, DocumentStatistics statistics)
    {
        _known = (document, statistics);
        _document = document;
    }

    /// <summary>
    /// Shows the properties of a node again after a change of its document, which makes the statistics given by
    /// <see cref="Know"/> out of date.
    /// </summary>
    public void Refresh(ModelNodeViewModel? node)
    {
        _known = null;
        Show(node);
    }

    public void Show(ModelNodeViewModel? node)
    {
        _node = node;
        IsEditable = node?.Entity is not null;
        List<MaterialChoice> choices = [MaterialChoice.ByBlock];
        if (node?.Entity is { } entity)
        {
            IEnumerable<Material> materials = _document?.Materials ?? [];
            if (entity.Material is { } own && !materials.Contains(own))
            {
                materials = materials.Append(own);
            }
            choices.AddRange(materials.Select(material => new MaterialChoice(material.Name, material)));
        }
        MaterialChoices = choices;
        OnPropertyChanged(nameof(EditedName));
        OnPropertyChanged(nameof(SelectedMaterial));
        HasSelection = node is not null;
        Title = node?.Name ?? string.Empty;
        Kind = node switch
        {
            null => string.Empty,
            { IsModel: true } => "Model",
            { IsReference: true } => "Block reference",
            { IsMesh: true } => "Mesh",
            { IsBrep: true } => "B-rep shape",
            _ => "Entity",
        };
        Rows = node switch
        {
            { Document: { } document } => ModelRows(
                _known is { } known && known.Document == document ? known.Statistics : DocumentStatistics.Of(document)),
            { Entity: BlockReference reference } => ReferenceRows(reference),
            { Entity: MeshEntity mesh } => MeshRows(mesh),
            { Entity: BrepEntity brep } => BrepRows(brep),
            _ => [],
        };
    }

    /// <summary>
    /// Shows the kind and the measures of a selected element, in the coordinates of its shape; for several, how many
    /// of each kind, and their total length or area when all are edges or all faces. None shows nothing.
    /// </summary>
    public void ShowElements(IReadOnlyList<ElementReference> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Show(null);
        if (elements.Count == 0)
        {
            return;
        }

        HasSelection = true;
        if (elements.Count == 1)
        {
            ElementReference element = elements[0];
            Title = element.Kind.ToString();
            Kind = $"of {element.Entity.Name ?? "a B-rep shape"}";
            Rows = ElementRows(element);
            return;
        }

        Title = Formatting.Count(elements.Count, "element", "elements");
        Kind = "Selection";
        List<PropertyRow> rows = [];
        foreach (IGrouping<ElementKind, ElementReference> group in elements.GroupBy(element => element.Kind))
        {
            rows.Add(new(group.Key switch
            {
                ElementKind.Face => "Faces",
                ElementKind.Edge => "Edges",
                _ => "Vertices",
            }, Formatting.Count(group.Count())));
        }
        if (elements.All(element => element.Kind == ElementKind.Edge))
        {
            rows.Add(new("Total length", $"{Formatting.Length(elements.Sum(Length))} mm"));
        }
        else if (elements.All(element => element.Kind == ElementKind.Face))
        {
            rows.Add(new("Total area", Formatting.Area(elements.Sum(element => FaceOf(element).Area))));
        }
        Rows = rows;
    }

    private static double Length(ElementReference edge) =>
        edge.Entity.Kernel.GetEdgeLength(edge.Entity.Shape, edge.Index);

    private static FaceInfo FaceOf(ElementReference face) => face.Entity.Kernel.GetFaceInfo(face.Entity.Shape, face.Index);

    private static List<PropertyRow> ElementRows(ElementReference element)
    {
        switch (element.Kind)
        {
            case ElementKind.Face:
                FaceInfo face = FaceOf(element);
                List<PropertyRow> rows = [new("Area", Formatting.Area(face.Area))];
                if (face.Plane is { } plane)
                {
                    rows.Add(new("Surface", "Plane"));
                    rows.Add(new("Plane", Formatting.Plane(plane)));
                    rows.Add(new("Normal", Formatting.Direction(plane.Normal)));
                }
                else
                {
                    rows.Add(new("Surface", "Curved"));
                }
                return rows;
            case ElementKind.Edge:
                return [new("Length", $"{Formatting.Length(Length(element))} mm")];
            default:
                Point3d point = element.Entity.Kernel.GetVertexPoint(element.Entity.Shape, element.Index);
                return [new("Position", Formatting.Point(point.X, point.Y, point.Z))];
        }
    }

    private static List<PropertyRow> ModelRows(DocumentStatistics statistics)
    {
        List<PropertyRow> rows =
        [
            new("Blocks", Formatting.Count(statistics.Blocks)),
            new("Block references", Formatting.Count(statistics.BlockReferences)),
        ];
        // A model made only of shapes has no meshes to count.
        if (statistics.Meshes > 0 || statistics.BrepShapes == 0)
        {
            rows.Add(new("Meshes",
                $"{Formatting.Count(statistics.Meshes)} ({Formatting.Count(statistics.PlacedMeshes)} placed)"));
            rows.Add(new("Triangles",
                $"{Formatting.Count(statistics.Triangles)} ({Formatting.Count(statistics.PlacedTriangles)} placed)"));
        }
        if (statistics.BrepShapes > 0)
        {
            rows.Add(new("B-rep shapes",
                $"{Formatting.Count(statistics.BrepShapes)} ({Formatting.Count(statistics.PlacedBrepShapes)} placed)"));
        }
        rows.Add(new("Materials", Formatting.Count(statistics.Materials)));
        rows.Add(new("Textures", Formatting.Count(statistics.Textures)));
        rows.Add(new("Size", Formatting.Size(statistics.BoundingBox)));
        return rows;
    }

    private static List<PropertyRow> ReferenceRows(BlockReference reference)
    {
        Transform transform = reference.Transform;
        return
        [
            new("Block", reference.Block.Name),
            new("Position", Formatting.Point(transform.M03, transform.M13, transform.M23)),
            new("Entities", Formatting.Count(reference.Block.Entities.Count)),
            new("Size", Formatting.Size(reference.GetBoundingBox())),
        ];
    }

    private static List<PropertyRow> MeshRows(MeshEntity mesh) =>
    [
        new("Vertices", Formatting.Count(mesh.Vertices.Length)),
        new("Triangles", Formatting.Count(mesh.Triangles.Length)),
        new("Normals", mesh.Normals is null ? "No" : "Yes"),
        new("Texture coordinates", mesh.TextureCoordinates is null ? "No" : "Yes"),
        new("Size", Formatting.Size(mesh.GetBoundingBox())),
    ];

    private static List<PropertyRow> BrepRows(BrepEntity brep) =>
    [
        new("Volume", Formatting.Volume(brep.Kernel.ComputeVolume(brep.Shape))),
        new("Size", Formatting.Size(brep.GetBoundingBox())),
    ];

}
