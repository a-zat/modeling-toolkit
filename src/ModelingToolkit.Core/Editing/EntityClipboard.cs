using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// The entity cut or copied, kept as it was then, with where its block was in the model, to be pasted into a block of
/// the same document at the same place in the model.
/// </summary>
public sealed class EntityClipboard
{
    private Entity? _entity;
    private Transform _placement = Transform.Identity;
    private Document? _document;

    /// <summary>Whether there is an entity to paste into a document.</summary>
    public bool CanPaste(Document document) => _entity is not null && ReferenceEquals(document, _document);

    /// <summary>Keeps a copy of the last entity of a path from the model of a document.</summary>
    public void Copy(Document document, IReadOnlyList<Entity> path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("The path is empty.", nameof(path));
        }
        _placement = path.Take(path.Count - 1).OfType<BlockReference>()
            .Aggregate(Transform.Identity, (placement, reference) => placement * reference.Transform);
        _entity = TransformCommands.Duplicate(path[^1], Transform.Identity);
        _document = document;
    }

    /// <summary>
    /// The command that pastes a new copy into the block being edited, at the place in the model it was copied from,
    /// and the copy; null when there is nothing to paste, or the copy is a reference that would put a block in itself.
    /// </summary>
    public (IEditCommand Command, Entity Pasted)? Paste(Document document, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanPaste(document) || _entity is BlockReference reference && reference.Block.Reaches(context.Block))
        {
            return null;
        }
        Entity pasted = TransformCommands.Duplicate(_entity!, context.Placement.Inverse() * _placement);
        return (new AddEntityCommand("Paste", context.Block, pasted), pasted);
    }
}
