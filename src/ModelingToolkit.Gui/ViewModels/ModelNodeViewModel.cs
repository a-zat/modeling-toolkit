using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// A node of the model tree: the model itself, a block reference, a mesh or a B-rep shape, as placed in the model.
/// </summary>
/// <remarks>
/// A block placed twice appears twice. The children of a node are created when the tree first asks for them, so that
/// only the expanded part of a large model is built.
/// </remarks>
public sealed partial class ModelNodeViewModel : ObservableObject
{
    private readonly Block? _block;
    private IReadOnlyList<ModelNodeViewModel>? _children;

    private ModelNodeViewModel(
        string name, string? detail, Entity? entity, IReadOnlyList<Entity> path, Block? block, Document? document)
    {
        _name = name;
        _detail = detail;
        Entity = entity;
        Path = path;
        Document = document;
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

    /// <summary>Secondary text: the number of triangles of a mesh, or the block of a reference with its own name.</summary>
    public string? Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    private string _name;
    private string? _detail;

    /// <summary>The entity of the node; null for the model.</summary>
    public Entity? Entity { get; }

    /// <summary>
    /// The entities from the model to the node, the last one being <see cref="Entity"/>: the references that place it,
    /// which tell apart the placements of a block. Empty for the model.
    /// </summary>
    public IReadOnlyList<Entity> Path { get; }

    /// <summary>The document, for the model node only.</summary>
    public Document? Document { get; }

    public bool IsModel => Entity is null;

    public bool IsReference => Entity is BlockReference;

    public bool IsMesh => Entity is MeshEntity;

    public bool IsBrep => Entity is BrepEntity;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public IReadOnlyList<ModelNodeViewModel> Children =>
        _children ??= _block is null ? [] : [.. _block.Entities.Select(Child)];

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
            Dictionary<Entity, ModelNodeViewModel> kept = _children.ToDictionary(
                child => child.Entity!, child => child, ReferenceEqualityComparer.Instance as IEqualityComparer<Entity>);
            _children = [.. changed.Entities.Select(entity => kept.GetValueOrDefault(entity) ?? Child(entity))];
            OnPropertyChanged(nameof(Children));
        }
        else if (change is Core.Model.PropertyChanged renamed && Entity is not null &&
                 (ReferenceEquals(renamed.Target, Entity) || ReferenceEquals(renamed.Target, _block)))
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
            if (current._children?.FirstOrDefault(child => ReferenceEquals(child.Entity, entity)) is not { } next)
            {
                return false;
            }
            current = next;
        }
        return ReferenceEquals(current, node);
    }

    private ModelNodeViewModel Child(Entity entity)
    {
        (string name, string? detail) = Label(entity);
        return new(name, detail, entity, [.. Path, entity], (entity as BlockReference)?.Block, null);
    }

    private static (string Name, string? Detail) Label(Entity entity)
    {
        switch (entity)
        {
            case BlockReference reference:
                string name = entity.Name ?? reference.Block.Name;
                return (name, name == reference.Block.Name ? null : reference.Block.Name);
            case MeshEntity mesh:
                // The meshes read from glTF files have no names: their materials tell them apart.
                return (entity.Name ?? entity.Material?.Name ?? "Mesh", $"{Formatting.Count(mesh.Triangles.Length)} triangles");
            case BrepEntity:
                return (entity.Name ?? entity.Material?.Name ?? "Shape", null);
            default:
                return (entity.Name ?? entity.GetType().Name, null);
        }
    }
}
