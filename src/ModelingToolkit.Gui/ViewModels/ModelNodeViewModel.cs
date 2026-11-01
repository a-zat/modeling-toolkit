using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A node of the model tree: the model itself, a block reference or a mesh, as placed in the model.</summary>
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
        Name = name;
        Detail = detail;
        Entity = entity;
        Path = path;
        Document = document;
        _block = block;
    }

    public string Name { get; }

    /// <summary>Secondary text: the number of triangles of a mesh, or the block of a reference with its own name.</summary>
    public string? Detail { get; }

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

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public IReadOnlyList<ModelNodeViewModel> Children =>
        _children ??= _block is null ? [] : [.. _block.Entities.Select(Child)];

    public static ModelNodeViewModel ForDocument(Document document) =>
        new(document.Root.Name, null, null, [], document.Root, document) { IsExpanded = true };

    private ModelNodeViewModel Child(Entity entity)
    {
        Entity[] path = [.. Path, entity];
        switch (entity)
        {
            case BlockReference reference:
                string name = entity.Name ?? reference.Block.Name;
                string? block = name == reference.Block.Name ? null : reference.Block.Name;
                return new(name, block, entity, path, reference.Block, null);
            case MeshEntity mesh:
                return new(entity.Name ?? "Mesh", $"{Formatting.Count(mesh.Triangles.Length)} triangles", entity, path,
                    null, null);
            default:
                return new(entity.Name ?? entity.GetType().Name, null, entity, path, null, null);
        }
    }
}
