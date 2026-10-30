using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>An occurrence of a <see cref="Model.Block"/> placed with a transformation, like an instance of a component.</summary>
public sealed class BlockReference : Entity
{
    public BlockReference(Block block, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(transform);

        Block = block;
        Transform = transform;
    }

    /// <summary>The referenced block; it cannot change, so that a reference never turns a block into a cycle.</summary>
    public Block Block { get; }

    /// <summary>From the coordinates of <see cref="Block"/> to those of the block containing this reference.</summary>
    public Transform Transform { get; set; }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) =>
        Block.ComputeBoundingBox(blockBoxes).Transform(Transform);
}
