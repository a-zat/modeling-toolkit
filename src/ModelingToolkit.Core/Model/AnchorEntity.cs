using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>What an <see cref="AnchorEntity"/> is for.</summary>
public enum AnchorKind
{
    /// <summary>A place that another component can follow, by an <see cref="Attachment"/>.</summary>
    Attach,

    /// <summary>
    /// The eyes of a person using the component, such as one sitting on a chair: its origin is where the eyes are, its X
    /// axis where they look.
    /// </summary>
    PointOfView,
}

/// <summary>
/// A named place in a block, with an orientation and no geometry: the end of a table where a chair stands, the seat of a
/// chair from which to look around.
/// </summary>
public sealed class AnchorEntity : Entity
{
    /// <param name="placement">
    /// From the frame of the anchor to the coordinates of its block: the origin is the place, the X axis the way it
    /// faces, the Z axis up.
    /// </param>
    /// <exception cref="ArgumentException">The kind is not one of <see cref="AnchorKind"/>.</exception>
    public AnchorEntity(Transform placement, AnchorKind kind = AnchorKind.Attach)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException($"Unknown anchor kind {kind}.", nameof(kind));
        }
        Placement = placement;
        Kind = kind;
    }

    public Transform Placement { get; }

    public AnchorKind Kind { get; }

    /// <summary>The place, in the coordinates of the block.</summary>
    public Point3d Point => Placement.Apply(Point3d.Origin);

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) => BoundingBox.Empty;
}
