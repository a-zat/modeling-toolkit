using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// Moves a block reference into another block, as a part dragged into a group, where it stays in the project: its
/// transformation becomes one from the new block. Undoing puts it back where it was.
/// </summary>
public sealed class MoveToBlockCommand : IEditCommand
{
    private readonly BlockReference _reference;
    private readonly Block _from;
    private readonly Block _to;
    private readonly int _index;
    private readonly Transform _before;
    private readonly Transform _after;
    private readonly int? _insertAt;

    /// <param name="reference">The reference moved, an entity of its block.</param>
    /// <param name="fromPlacement">From the coordinates of the block it leaves to those of the project.</param>
    /// <param name="to">The block it goes into.</param>
    /// <param name="toPlacement">From the coordinates of that block to those of the project.</param>
    /// <param name="insertAt">
    /// Where it goes among the entities of that block, as there when applied; null for after them. References moved
    /// together, from the last to the first, keep their order when all go at the same place.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The reference is in no block, it already is in <paramref name="to"/>, or <paramref name="to"/> is its block or in
    /// it, which would place a block in itself.
    /// </exception>
    public MoveToBlockCommand(
        BlockReference reference, Transform fromPlacement, Block to, Transform toPlacement, int? insertAt = null)
    {
        _insertAt = insertAt;
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(fromPlacement);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(toPlacement);
        _from = reference.Owner ?? throw new ArgumentException("The reference is in no block.", nameof(reference));
        if (ReferenceEquals(_from, to))
        {
            throw new ArgumentException("The reference is already in that block.", nameof(to));
        }
        if (Places(reference.Block, to))
        {
            throw new ArgumentException($"{to.Name} is in {reference.Block.Name}: it cannot take it.", nameof(to));
        }
        _reference = reference;
        _to = to;
        _index = _from.Entities.IndexOf(reference);
        _before = reference.Transform;
        _after = toPlacement.Inverse() * fromPlacement * reference.Transform;
    }

    public string Name => "Move to Block";

    public void Apply()
    {
        _from.Entities.Remove(_reference);
        _reference.Transform = _after;
        if (_insertAt is { } at)
        {
            _to.Entities.Insert(Math.Clamp(at, 0, _to.Entities.Count), _reference);
        }
        else
        {
            _to.Entities.Add(_reference);
        }
    }

    public void Revert()
    {
        _to.Entities.Remove(_reference);
        _reference.Transform = _before;
        _from.Entities.Insert(_index, _reference);
    }

    /// <summary>Whether a block is, or places somewhere inside it, another block.</summary>
    public static bool Places(Block block, Block other)
    {
        ArgumentNullException.ThrowIfNull(block);
        var seen = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Block>([block]);
        while (pending.TryPop(out Block? current))
        {
            if (ReferenceEquals(current, other))
            {
                return true;
            }
            if (seen.Add(current))
            {
                foreach (BlockReference child in current.Entities.OfType<BlockReference>())
                {
                    pending.Push(child.Block);
                }
            }
        }
        return false;
    }
}
