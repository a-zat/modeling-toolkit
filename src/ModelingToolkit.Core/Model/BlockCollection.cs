using System.Collections.ObjectModel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// The blocks of a document. A block belongs to one document at most, so that its changes reach that document.
/// </summary>
public sealed class BlockCollection : Collection<Block>
{
    private readonly Document _owner;

    internal BlockCollection(Document owner)
    {
        _owner = owner;
    }

    public void AddRange(IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        foreach (Block block in blocks)
        {
            Add(block);
        }
    }

    protected override void InsertItem(int index, Block item)
    {
        Adopt(item);
        base.InsertItem(index, item);
        _owner.Raise(new BlockAdded(item));
    }

    protected override void SetItem(int index, Block item)
    {
        Block old = this[index];
        if (ReferenceEquals(old, item))
        {
            return;
        }

        Adopt(item);
        base.SetItem(index, item);
        old.Document = null;
        _owner.Raise(new BlockRemoved(old));
        _owner.Raise(new BlockAdded(item));
    }

    protected override void RemoveItem(int index)
    {
        Block old = this[index];
        base.RemoveItem(index);
        old.Document = null;
        _owner.Raise(new BlockRemoved(old));
    }

    protected override void ClearItems()
    {
        while (Count > 0)
        {
            RemoveItem(Count - 1);
        }
    }

    private void Adopt(Block item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Document is not null)
        {
            throw new InvalidOperationException($"Block '{item.Name}' already belongs to a document.");
        }

        item.Document = _owner;
    }
}
