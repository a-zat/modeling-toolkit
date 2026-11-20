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

    /// <summary>The movements of the selection, shown in a section of its properties.</summary>
    public AnimationsViewModel? Animations { get; set; }

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

    /// <summary>Whether the selection has a field to edit: the name of its block or its material.</summary>
    public bool HasFields => HasBlock || HasMaterial;

    private Surfaces[] _surfaces = [];

    /// <summary>The name of the entity, as typed: setting it renames the entity, or takes its name away when empty.</summary>
    public string EditedName
    {
        get => _node is { IsModel: true, Document: { } model } ? model.Root.Name : _node?.Entity?.Name ?? string.Empty;
        set
        {
            string? name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_node is { IsModel: true, Document: { } model })
            {
                // The model has the name of its scene in glTF: it always has one.
                if (name is not null && name != model.Root.Name)
                {
                    Edit?.Invoke(new RenameBlockCommand(model.Root, name));
                }
            }
            else if (_node?.Entity is { } entity && name != entity.Name)
            {
                Edit?.Invoke(new RenameCommand(entity, name, _document));
            }
            OnPropertyChanged(); // the name the entity has, if the edit was refused or trimmed
        }
    }

    /// <summary>Whether the title is being edited, to rename the entity: a double click on it begins.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>Begins to edit the title, when the selection is an entity.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void StartRename() => IsRenaming = IsEditable;

    /// <summary>Whether the selection has a Movement section: components and groups, not the project or a mesh.</summary>
    [ObservableProperty]
    public partial bool ShowsMovement { get; private set; }

    /// <summary>Whether the selection is a reference, whose block has a name of its own to edit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFields))]
    public partial bool HasBlock { get; private set; }

    /// <summary>
    /// The name of the block of the reference selected, as typed: setting it renames the block, which every reference to
    /// it without a name of its own shows.
    /// </summary>
    public string BlockName
    {
        get => (_node?.Entity as BlockReference)?.Block.Name ?? string.Empty;
        set
        {
            if (_node?.Entity is BlockReference { Block: var block } && !string.IsNullOrWhiteSpace(value) &&
                value.Trim() != block.Name)
            {
                try
                {
                    Edit?.Invoke(new RenameBlockCommand(block, value, _document));
                }
                catch (EditRefusedException exception)
                {
                    Refused?.Invoke(exception.Message);
                }
            }
            OnPropertyChanged();
        }
    }

    /// <summary>Tells why an edit of a field was refused, as the status bar does.</summary>
    public Action<string>? Refused { get; set; }

    /// <summary>Begins to place an anchor or a point of view on the component selected, by a click in the 3D view.</summary>
    public Action<AnchorKind>? PlaceAnchor { get; set; }

    /// <summary>Looks from a point of view of the component selected, walking.</summary>
    public Action<AnchorRowViewModel>? LookFrom { get; set; }

    /// <summary>
    /// Whether the selection is a component, not a group, which has anchors and points of view, and can follow an anchor.
    /// </summary>
    [ObservableProperty]
    public partial bool HasAnchors { get; private set; }

    /// <summary>The anchors and the points of view of the block of the component selected.</summary>
    public System.Collections.ObjectModel.ObservableCollection<AnchorRowViewModel> Anchors { get; } = [];

    /// <summary>What the component selected can follow: nothing, or each anchor of the block where it is, at any depth.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AttachChoice> AttachChoices { get; private set; } = [];

    /// <summary>Whether there is an anchor to follow, or one followed, so that the choice is shown.</summary>
    [ObservableProperty]
    public partial bool CanAttach { get; private set; }

    /// <summary>The anchor that the component follows, or nothing, as its choice shows it.</summary>
    [ObservableProperty]
    public partial AttachChoice? Attached { get; private set; }

    // Makes the component follow the anchor chosen, from where it is, or nothing: one command.
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Attach(AttachChoice? choice)
    {
        if (choice is null || choice.IsChosen || _document is not { } document || _node?.Entity is not BlockReference component)
        {
            return;
        }
        List<IEditCommand> commands = [];
        if (document.AttachmentOf(component) is { } current)
        {
            commands.Add(new AttachCommand("Detach", document, current, attach: false));
        }
        if (choice.Attachment is { } attachment)
        {
            commands.Add(new AttachCommand("Attach", document, attachment, attach: true));
        }
        if (commands.Count > 0)
        {
            Edit?.Invoke(commands.Count == 1 ? commands[0] : new CompositeCommand("Attach", commands));
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void AddAnchor() => PlaceAnchor?.Invoke(AnchorKind.Attach);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void AddPointOfView() => PlaceAnchor?.Invoke(AnchorKind.PointOfView);

    // The anchors of the component, and what it can follow.
    private void ShowAnchors(ModelNodeViewModel? node)
    {
        Anchors.Clear();
        // A group gathers components, as a room its furniture: what it holds has anchors, not the group.
        if (node is not { Entity: BlockReference { IsGroup: false } component, Path: { } path } || Edit is not { } edit)
        {
            HasAnchors = false;
            CanAttach = false;
            AttachChoices = [];
            Attached = null;
            return;
        }
        HasAnchors = true;
        foreach (AnchorEntity anchor in component.Block.Entities.OfType<AnchorEntity>())
        {
            Anchors.Add(new AnchorRowViewModel(anchor, path, edit, LookFrom));
        }
        Attachment? current = _document?.AttachmentOf(component) is { IsActive: true } active ? active : null;
        var choices = new List<AttachChoice>
        {
            new("Nothing", null) { Choose = AttachCommand, IsChosen = current is null },
        };
        foreach (Attachment candidate in Followable(component))
        {
            string holder = candidate.Chain.Count > 0
                ? candidate.Chain[^1].Name ?? candidate.Chain[^1].Block.Name
                : "this block";
            bool chosen = current is not null && ReferenceEquals(current.Anchor, candidate.Anchor) &&
                current.Chain.SequenceEqual(candidate.Chain);
            choices.Add(new AttachChoice($"{candidate.Anchor.Name ?? "Anchor"} of {holder}", candidate)
            {
                Choose = AttachCommand,
                IsChosen = chosen,
            });
        }
        if (current is not null && !choices.Exists(choice => choice.IsChosen))
        {
            // Followed from further away than the block of the component, as a file may say.
            choices.Add(new AttachChoice(current.Anchor.Name ?? "Anchor", null) { IsChosen = true });
        }
        AttachChoices = choices;
        Attached = choices.First(choice => choice.IsChosen);
        CanAttach = choices.Count > 1;
    }

    // The anchors to attach to in the block holding the component and in the components there, at any depth, but those
    // inside the component itself; each as the attachment that keeps the component where it is.
    private static List<Attachment> Followable(BlockReference component)
    {
        var found = new List<Attachment>();
        if (component.Owner is not { } owner)
        {
            return found;
        }
        var chain = new List<BlockReference>();
        void Look(Block block)
        {
            foreach (Entity entity in block.Entities)
            {
                if (found.Count >= 200)
                {
                    return;
                }
                if (entity is AnchorEntity { Kind: AnchorKind.Attach } anchor)
                {
                    found.Add(new Attachment(component, [.. chain], anchor, Attachment.OffsetOf(component, chain, anchor)));
                }
                else if (entity is BlockReference reference && !ReferenceEquals(reference, component) && chain.Count < 32)
                {
                    chain.Add(reference);
                    Look(reference.Block);
                    chain.RemoveAt(chain.Count - 1);
                }
            }
        }
        Look(owner);
        return found;
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
        IsRenaming = false;
        IsEditable = node?.Entity is not null || node is { IsModel: true };
        HasBlock = node?.Entity is BlockReference;
        ShowsMovement = node?.Entity is BlockReference;
        OnPropertyChanged(nameof(BlockName));
        ShowAnchors(node);
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
            { IsGroup: true } => "Group",
            { IsReference: true } => "Component",
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
