using System.Collections.ObjectModel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// The entities of a block. It rejects null entries and references that would make the block contain itself,
/// so that every traversal of a document terminates.
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
    }

    protected override void SetItem(int index, Entity item)
    {
        Validate(item);
        base.SetItem(index, item);
    }

    private void Validate(Entity item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is BlockReference reference && reference.Block.Reaches(_owner))
        {
            throw new InvalidOperationException(
                $"Block '{_owner.Name}' cannot reference block '{reference.Block.Name}': it would contain itself.");
        }
    }
}
