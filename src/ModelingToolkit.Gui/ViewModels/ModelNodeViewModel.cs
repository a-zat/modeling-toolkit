using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// A node of the model tree, an outline of the references of the blocks: the model itself, a block reference, the
/// geometry of a block, or a mesh or a B-rep shape of that geometry, as placed in the model.
/// </summary>
/// <remarks>
/// The children of the model and of a reference are the references of its block, then the entities of the block that
/// are not references: the entity itself when there is one, as there is when the geometry of the block is one shape;
/// a geometry node with them, when there are several, such as the meshes of a glTF node. A block placed twice appears
/// twice. The children of a node are created when the tree first asks for them,
/// so that only the expanded part of a large model is built.
/// </remarks>
public sealed partial class ModelNodeViewModel : ObservableObject
{
    private readonly Block? _block;
    private IReadOnlyList<ModelNodeViewModel>? _children;

    private ModelNodeViewModel(
        string name, string? detail, Entity? entity, IReadOnlyList<Entity> path, Block? block, Document? document,
        bool isGeometry = false)
    {
        _name = name;
        _detail = detail;
        Entity = entity;
        Path = path;
        Document = document;
        IsGeometry = isGeometry;
        _block = block;
    }

    /// <summary>
    /// The name of the entity; a block reference without one takes the name of its block, a mesh or a shape the name
    /// of its material.
    /// </summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Secondary text: the number of triangles of a mesh, the block of a reference with its own name, or the counts of
    /// a geometry.
    /// </summary>
    public string? Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(DetailSpaced));
            }
        }
    }

    private string _name;
    private string? _detail;

    /// <summary>The entity of the node; null for the model and for a geometry.</summary>
    public Entity? Entity { get; }

    /// <summary>
    /// The entities from the model to the node, the last one being <see cref="Entity"/>: the references that place it,
    /// which tell apart the placements of a block. Empty for the model; for a geometry, the path of the node it is the
    /// geometry of.
    /// </summary>
    public IReadOnlyList<Entity> Path { get; }

    /// <summary>The document, for the model node only.</summary>
    public Document? Document { get; }

    /// <summary>The block whose entities the node shows: of the model, of a reference, or of a geometry.</summary>
    public Block? Block => _block;

    public bool IsModel => Document is not null;

    /// <summary>Whether the node is the geometry of a block: its entities that are not references.</summary>
    public bool IsGeometry { get; }

    /// <summary>Whether the node is the reference whose block is being edited.</summary>
    [ObservableProperty]
    public partial bool IsContext { get; set; }

    public bool IsReference => Entity is BlockReference;

    /// <summary>Whether it is a group, as a room: see <see cref="BlockReference.IsGroup"/>.</summary>
    public bool IsGroup => Entity is BlockReference { IsGroup: true };

    /// <summary>A reference that is a component, not a group.</summary>
    public bool IsComponent => Entity is BlockReference { IsGroup: false };

    /// <summary>Whether the node is a reference inside the block of another one, which it can be moved out of.</summary>
    public bool CanMoveOut => ModelTreeViewModel.CanMoveOut(this);

    /// <summary>The name of the reference whose block this node is in, for "Move out of …".</summary>
    public string MoveOutLabel => Path.Count >= 2 && Path[^2] is BlockReference parent
        ? $"Move out of {parent.Name ?? parent.Block.Name}"
        : "Move out";

    /// <summary>Whether a block can be added under the node: the model's, or a reference's.</summary>
    public bool CanHoldBlocks => (IsModel || IsReference) && Block is not null;

    /// <summary>The detail after the name, with the space between them; empty for none.</summary>
    public string DetailSpaced => string.IsNullOrEmpty(Detail) ? string.Empty : "  " + Detail;

    public bool IsMesh => Entity is MeshEntity;

    public bool IsBrep => Entity is BrepEntity;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Whether the 3D view shows the entity; the project and the geometries are always shown.</summary>
    public bool IsVisible => Entity is not { IsHidden: true };

    /// <summary>Whether the node can be renamed, and shown or hidden: one of an entity.</summary>
    public bool HasEntity => Entity is not null;

    /// <summary>Whether the name is being edited in the outline, as <see cref="EditedName"/>.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string EditedName { get; set; } = string.Empty;

    public IReadOnlyList<ModelNodeViewModel> Children => _children ??= BuildChildren([]);

    public static ModelNodeViewModel ForDocument(Document document) =>
        new(document.Root.Name, null, null, [], document.Root, document) { IsExpanded = true };

    /// <summary>
    /// Follows a change of the document in the part of the tree built so far: the children of the nodes of a block
    /// that changed, reusing the nodes of the entities that remain, so that they stay open, and the names of the nodes
    /// of an entity or a block that was renamed.
    /// </summary>
    internal void Apply(DocumentChange change)
    {
        Block? changed = change switch
        {
            EntityAdded added => added.Block,
            EntityRemoved removed => removed.Block,
            EntityReplaced replaced => replaced.Block,
            _ => null,
        };
        if (changed is not null && ReferenceEquals(_block, changed) && _children is not null)
        {
            _children = BuildChildren(_children);
            OnPropertyChanged(nameof(Children));
        }
        else if (change is Core.Model.PropertyChanged renamed && Entity is not null &&
                 (ReferenceEquals(renamed.Target, Entity) || ReferenceEquals(renamed.Target, _block)))
        {
            (Name, Detail) = Label(Entity);
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(IsGroup));
            OnPropertyChanged(nameof(IsComponent));
        }
        if (IsGeometry && ReferenceEquals(changed, _block))
        {
            Detail = GeometryDetail(_block!);
        }
        else if (change is ShapeReplaced replacedShape && ReferenceEquals(replacedShape.Entity, Entity))
        {
            (Name, Detail) = Label(Entity);
        }

        foreach (ModelNodeViewModel child in _children ?? [])
        {
            child.Apply(change);
        }
    }

    /// <summary>Whether the node is still in the part of the tree built so far, given its path.</summary>
    internal bool Contains(ModelNodeViewModel node)
    {
        ModelNodeViewModel current = this;
        foreach (Entity entity in node.Path)
        {
            if (current.FindChild(entity, build: false) is not { } next)
            {
                return false;
            }
            current = next;
        }
        return node.IsGeometry
            ? current._children?.Any(child => ReferenceEquals(child, node)) == true
            : ReferenceEquals(current, node);
    }

    /// <summary>
    /// The child of an entity of the block: a node of a reference, or of an entity in the geometry, which is then
    /// opened when <paramref name="build"/> builds the nodes not built yet.
    /// </summary>
    internal ModelNodeViewModel? FindChild(Entity entity, bool build)
    {
        foreach (ModelNodeViewModel child in (build ? Children : _children) ?? [])
        {
            if (ReferenceEquals(child.Entity, entity))
            {
                return child;
            }
            if (child.IsGeometry && entity is not BlockReference && child.FindChild(entity, build) is { } found)
            {
                if (build)
                {
                    child.IsExpanded = true;
                }
                return found;
            }
        }
        return null;
    }

    // The nodes of the entities of the block, reusing those of the entities that remain, so that they stay open.
    private List<ModelNodeViewModel> BuildChildren(IReadOnlyList<ModelNodeViewModel> previous)
    {
        if (_block is null)
        {
            return [];
        }

        Dictionary<Entity, ModelNodeViewModel> kept = previous.Where(child => child.Entity is not null).ToDictionary(
            child => child.Entity!, child => child, ReferenceEqualityComparer.Instance as IEqualityComparer<Entity>);
        if (IsGeometry)
        {
            return [.. _block.Entities.Where(entity => entity is not BlockReference)
                .Select(entity => kept.GetValueOrDefault(entity) ?? Child(entity))];
        }

        List<ModelNodeViewModel> children = [.. _block.Entities.OfType<BlockReference>()
            .Select(entity => kept.GetValueOrDefault(entity) ?? Child(entity))];
        Entity[] geometry = [.. _block.Entities.Where(entity => entity is not BlockReference)];
        if (geometry.Length == 1)
        {
            children.Add(kept.GetValueOrDefault(geometry[0]) ?? Child(geometry[0]));
        }
        else if (geometry.Length > 1)
        {
            children.Add(previous.FirstOrDefault(child => child.IsGeometry) ??
                new ModelNodeViewModel("Geometry", GeometryDetail(_block), null, Path, _block, null, isGeometry: true));
        }
        return children;
    }

    // What the entities of a block that are not references are: "2 shapes · 3 meshes".
    private static string GeometryDetail(Block block)
    {
        List<string> parts = [];
        int shapes = block.Entities.Count(entity => entity is BrepEntity);
        int meshes = block.Entities.Count(entity => entity is MeshEntity);
        if (shapes > 0)
        {
            parts.Add(Formatting.Count(shapes, "shape", "shapes"));
        }
        if (meshes > 0)
        {
            parts.Add(Formatting.Count(meshes, "mesh", "meshes"));
        }
        return string.Join(" · ", parts);
    }

    // The solids and faces of a shape: "1 solid · 7 faces".
    private static string ShapeDetail(BrepEntity shape)
    {
        ShapeTopology counts = shape.Kernel.CountElements(shape.Shape);
        return $"{Formatting.Count(counts.Solids, "solid", "solids")} · {Formatting.Count(counts.Faces, "face", "faces")}";
    }

    private ModelNodeViewModel Child(Entity entity)
    {
        (string name, string? detail) = Label(entity);
        return new(name, detail, entity, [.. Path, entity], (entity as BlockReference)?.Block, null);
    }

    internal static (string Name, string? Detail) Label(Entity entity)
    {
        switch (entity)
        {
            case BlockReference reference:
                string name = entity.Name ?? reference.Block.Name;
                return (name, name == reference.Block.Name ? null : reference.Block.Name);
            case MeshEntity mesh:
                // The meshes read from glTF files have no names: their materials tell them apart.
                return (entity.Name ?? mesh.Material?.Name ?? "Mesh", $"{Formatting.Count(mesh.Triangles.Length)} triangles");
            case BrepEntity shape:
                return (entity.Name ?? "Geometry", ShapeDetail(shape));
            case GuideEntity:
                return (entity.Name ?? "Guide", null);
            case AnchorEntity anchor:
                return (entity.Name ?? (anchor.Kind == AnchorKind.PointOfView ? "Point of view" : "Anchor"), null);
            default:
                return (entity.Name ?? entity.GetType().Name, null);
        }
    }
}
