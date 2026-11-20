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
        _transform = transform;
    }

    private Transform _transform;

    /// <summary>The referenced block; it cannot change, so that a reference never turns a block into a cycle.</summary>
    public Block Block { get; }

    /// <summary>From the coordinates of <see cref="Block"/> to those of the block containing this reference.</summary>
    public Transform Transform
    {
        get => _transform;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(_transform, value))
            {
                _transform = value;
                Raise(new PropertyChanged(this, nameof(Transform)));
            }
        }
    }

    /// <summary>
    /// Whether it groups what it places, as a room its furniture, rather than being a component: a click in the 3D view
    /// selects what is inside it, and the outline selects it as a whole.
    /// </summary>
    public bool IsGroup
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Raise(new PropertyChanged(this, nameof(IsGroup)));
            }
        }
    }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) =>
        Block.ComputeBoundingBox(blockBoxes).Transform(Transform);
}
