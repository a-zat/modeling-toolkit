using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A named group of entities, in its own coordinate system, that can be placed many times through
/// <see cref="BlockReference"/>s: geometry used in several places exists only once.
/// </summary>
public sealed class Block
{
    public Block(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Entities = new EntityCollection(this);
    }

    public string Name
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (field != value)
            {
                field = value;
                Raise(new PropertyChanged(this, nameof(Name)));
            }
        }
    }

    /// <summary>The document that lists the block, as its root or in its blocks; null for none.</summary>
    public Document? Document { get; internal set; }

    public EntityCollection Entities { get; }

    internal void Raise(DocumentChange change) => Document?.Raise(change);

    public BoundingBox GetBoundingBox() => ComputeBoundingBox(new Dictionary<Block, BoundingBox>());

    internal BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes)
    {
        if (blockBoxes.TryGetValue(this, out BoundingBox box))
        {
            return box;
        }

        box = BoundingBox.Empty;
        foreach (Entity entity in Entities)
        {
            box = box.Union(entity.ComputeBoundingBox(blockBoxes));
        }
        blockBoxes[this] = box;
        return box;
    }

    /// <summary>Whether this block is <paramref name="target"/> or references it, directly or through other blocks.</summary>
    internal bool Reaches(Block target)
    {
        var visited = new HashSet<Block>();
        var pending = new Stack<Block>();
        pending.Push(this);
        while (pending.TryPop(out Block? block))
        {
            if (block == target)
            {
                return true;
            }
            if (!visited.Add(block))
            {
                continue;
            }
            foreach (Entity entity in block.Entities)
            {
                if (entity is BlockReference reference)
                {
                    pending.Push(reference.Block);
                }
            }
        }
        return false;
    }
}
