using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A change of a <see cref="Document"/>, raised by <see cref="Document.Changed"/> right after it happened, on the
/// thread that made it.
/// </summary>
/// <remarks>The set of changes is closed: only this assembly can derive from this record.</remarks>
public abstract record DocumentChange
{
    private protected DocumentChange()
    {
    }
}

/// <summary>An entity was inserted in a block, at <paramref name="Index"/>.</summary>
public sealed record EntityAdded(Block Block, int Index, Entity Entity) : DocumentChange;

/// <summary>An entity was removed from a block; <paramref name="Index"/> is where it was.</summary>
public sealed record EntityRemoved(Block Block, int Index, Entity Entity) : DocumentChange;

/// <summary>The entity at <paramref name="Index"/> of a block was replaced by another one.</summary>
public sealed record EntityReplaced(Block Block, int Index, Entity OldEntity, Entity NewEntity) : DocumentChange;

/// <summary>
/// The shape of a B-rep entity was replaced: the entity is the same, so the paths that reach it remain valid, but its
/// geometry, and the order of its faces, edges and vertices, are new.
/// </summary>
public sealed record ShapeReplaced(BrepEntity Entity, IKernelShape OldShape, IKernelShape NewShape) : DocumentChange;

/// <summary>A block was added to <see cref="Document.Blocks"/>.</summary>
public sealed record BlockAdded(Block Block) : DocumentChange;

/// <summary>A block was removed from <see cref="Document.Blocks"/>.</summary>
public sealed record BlockRemoved(Block Block) : DocumentChange;

/// <summary>
/// A property of an entity, a block or a layer changed: <see cref="Entity.Name"/>, <see cref="Entity.Layer"/>,
/// <see cref="MeshEntity.Material"/>, <see cref="BrepEntity.FaceMaterials"/>, <see cref="BlockReference.Transform"/>,
/// <see cref="Block.Name"/>, <see cref="Layer.Name"/> or <see cref="Layer.IsHidden"/>.
/// </summary>
/// <param name="Target">The entity, the block or the layer.</param>
/// <param name="Property">The name of the property, as <c>nameof</c> gives it.</param>
public sealed record PropertyChanged(object Target, string Property) : DocumentChange;

/// <summary>A layer was added to <see cref="Document.Layers"/>, or removed from it.</summary>
public sealed record LayersChanged(Layer Layer, bool Added) : DocumentChange;

/// <summary>An attachment was added to <see cref="Document.Attachments"/>, or removed from it.</summary>
public sealed record AttachmentsChanged(Attachment Attachment, bool Added) : DocumentChange;
