using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A row of the properties panel.</summary>
public sealed record PropertyRow(string Name, string Value);

/// <summary>Surfaces that take a material: a whole mesh, or faces of a shape.</summary>
internal readonly record struct Surfaces(MeshEntity? Mesh, BrepEntity? Shape, int[] Faces)
{
    public IEnumerable<Material?> Materials => Mesh is { } mesh ? [mesh.Material] : Faces.Select(Shape!.MaterialOf);

    public IEditCommand Paint(Material? material) => Mesh is { } mesh
        ? new SetMeshMaterialCommand(mesh, material)
        : new PaintFacesCommand(Shape!, Faces, material);
}

/// <summary>The properties of the selected node of the model tree, or of the faces, edges and vertices selected.</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    public PropertiesViewModel()
    {
        Picker.Chosen = Paint;
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

    /// <summary>Whether the selection is an entity, whose name can be changed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFields))]
    public partial bool IsEditable { get; private set; }

    /// <summary>
    /// Whether the selection has surfaces, whose material can be changed: a mesh, a shape, whose faces are all
    /// painted at once, or faces. Edges, vertices and references have none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFields))]
    public partial bool HasMaterial { get; private set; }

    /// <summary>Whether the selection has a field to edit: its name or its material.</summary>
    public bool HasFields => IsEditable || HasMaterial;

    private Surfaces[] _surfaces = [];

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

    /// <summary>The choice of a material for the surfaces selected.</summary>
    public MaterialPickerViewModel Picker { get; } = new();

    /// <summary>The material of the surfaces selected; null when they have none, or different ones.</summary>
    public Material? Material { get; private set; }

    /// <summary>Whether the surfaces selected have different materials.</summary>
    public bool IsMixedMaterial { get; private set; }

    /// <summary>The name of the material, "Default" or "Mixed".</summary>
    [ObservableProperty]
    public partial string MaterialName { get; private set; } = string.Empty;

    /// <summary>The swatch of the material.</summary>
    [ObservableProperty]
    public partial Avalonia.Media.IBrush? MaterialBrush { get; private set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenPicker() => Picker.Open(_document);

    /// <summary>
    /// Paints the surfaces selected with a material, null for the default one; a material that the document does not
    /// have yet is added to it, in the same command.
    /// </summary>
    public void Paint(Material? material)
    {
        if (_surfaces.Length == 0 ||
            _surfaces.SelectMany(surfaces => surfaces.Materials).All(current => ReferenceEquals(current, material)))
        {
            return;
        }

        List<IEditCommand> commands = [];
        if (material is not null && _document is { } document && !document.Materials.Contains(material))
        {
            commands.Add(new AddMaterialCommand(document, material));
        }
        commands.AddRange(_surfaces.Select(surfaces => surfaces.Paint(material)));
        Edit?.Invoke(commands.Count == 1 ? commands[0] : new CompositeCommand("Paint", commands));
    }

    // The material of the surfaces, as the row shows it.
    private void ShowMaterials(Surfaces[] surfaces)
    {
        _surfaces = surfaces;
        HasMaterial = surfaces.Length > 0;
        Material?[] materials = [.. surfaces.SelectMany(surface => surface.Materials).Distinct()];
        IsMixedMaterial = materials.Length > 1;
        Material = materials.Length == 1 ? materials[0] : null;
        MaterialName = IsMixedMaterial ? "Mixed" : Material?.Name ?? "Default";
        MaterialBrush = IsMixedMaterial
            ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xB4, 0xB9, 0xC2))
            : MaterialSwatch.BrushOf(Material);
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
        ShowMaterials(node?.Entity switch
        {
            MeshEntity mesh => [new Surfaces(mesh, null, [])],
            BrepEntity shape => [new Surfaces(null, shape, [.. Enumerable.Range(0, shape.Kernel.CountElements(shape.Shape).Faces)])],
            _ => [],
        });
        OnPropertyChanged(nameof(EditedName));
        HasSelection = node is not null;
        Title = node?.Name ?? string.Empty;
        Kind = node switch
        {
            null => string.Empty,
            { IsModel: true } => "Project",
            { IsGeometry: true, Block: { } geometryBlock } => $"of {geometryBlock.Name}",
            { IsReference: true } => "Block reference",
            { IsMesh: true } => "Mesh",
            { IsBrep: true } => "B-rep shape",
            _ => "Entity",
        };
        Rows = node switch
        {
            { Document: { } document } => ModelRows(
                _known is { } known && known.Document == document ? known.Statistics : DocumentStatistics.Of(document)),
            { IsGeometry: true, Block: { } block } => GeometryRows(block),
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
        // The faces selected take a material, shape by shape; edges and vertices have none.
        ShowMaterials([.. elements.Where(element => element.Kind == ElementKind.Face)
            .GroupBy(element => element.Entity)
            .Select(faces => new Surfaces(null, faces.Key, [.. faces.Select(face => face.Index).Distinct()]))]);
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
        edge.Entity.Kernel.GetEdgeInfo(edge.Entity.Shape, edge.Index).Length;

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
                EdgeInfo edge = element.Entity.Kernel.GetEdgeInfo(element.Entity.Shape, element.Index);
                List<PropertyRow> edgeRows =
                [
                    new("Length", $"{Formatting.Length(edge.Length)} mm"),
                    new("Curve", edge.Kind.ToString()),
                ];
                if (edge.Circle is { } circle)
                {
                    edgeRows.Add(new("Radius", $"{Formatting.Length(circle.Radius)} mm"));
                }
                return edgeRows;
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

    // The entities of a block that are not references, and what they are made of.
    private static List<PropertyRow> GeometryRows(Block block)
    {
        List<PropertyRow> rows = [];
        BrepEntity[] shapes = [.. block.Entities.OfType<BrepEntity>()];
        if (shapes.Length > 0)
        {
            ShapeTopology[] counts = [.. shapes.Select(shape => shape.Kernel.CountElements(shape.Shape))];
            rows.Add(new("B-rep shapes", Formatting.Count(shapes.Length)));
            rows.Add(new("Solids", Formatting.Count(counts.Sum(count => count.Solids))));
            rows.Add(new("Faces", Formatting.Count(counts.Sum(count => count.Faces))));
            rows.Add(new("Edges", Formatting.Count(counts.Sum(count => count.Edges))));
            rows.Add(new("Vertices", Formatting.Count(counts.Sum(count => count.Vertices))));
        }
        MeshEntity[] meshes = [.. block.Entities.OfType<MeshEntity>()];
        if (meshes.Length > 0)
        {
            rows.Add(new("Meshes", Formatting.Count(meshes.Length)));
            rows.Add(new("Triangles", Formatting.Count(meshes.Sum(mesh => (long)mesh.Triangles.Length))));
        }
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
