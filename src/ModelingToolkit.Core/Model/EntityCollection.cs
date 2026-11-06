using System.Collections.ObjectModel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// The entities of a block. It rejects null entries, entities that already belong to a block, and references that
/// would make the block contain itself, so that every traversal of a document terminates. Its changes are raised by
/// the document of the block.
/// </summary>
public sealed class EntityCollection : Collection<Entity>
{
    private readonly Block _owner;

    internal EntityCollection(Block owner)
    {
        _owner = owner;
    }

    protected override void InsertItem(int index, Entity item)
    {
        Validate(item);
        base.InsertItem(index, item);
        item.Owner = _owner;
        _owner.Raise(new EntityAdded(_owner, index, item));
    }

    protected override void SetItem(int index, Entity item)
    {
        Entity old = this[index];
        if (ReferenceEquals(old, item))
        {
            return;
        }

        Validate(item);
        base.SetItem(index, item);
        old.Owner = null;
        item.Owner = _owner;
        _owner.Raise(new EntityReplaced(_owner, index, old, item));
    }

    protected override void RemoveItem(int index)
    {
        Entity old = this[index];
        base.RemoveItem(index);
        old.Owner = null;
        _owner.Raise(new EntityRemoved(_owner, index, old));
    }

    // One removal at a time, from the end, so that each change gives the index the entity had.
    protected override void ClearItems()
    {
        while (Count > 0)
        {
            RemoveItem(Count - 1);
        }
    }

    private void Validate(Entity item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Owner is not null)
        {
            throw new InvalidOperationException(
                $"The entity already belongs to block '{item.Owner.Name}': remove it from there first.");
        }
        if (item is BlockReference reference && reference.Block.Reaches(_owner))
        {
            throw new InvalidOperationException(
                $"Block '{_owner.Name}' cannot reference block '{reference.Block.Name}': it would contain itself.");
        }
    }
}
