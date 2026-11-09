using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A guide line, endless, that drawing snaps to: through a point, along a direction, in the coordinates of its block.
/// It has no size, so it takes no part in the bounding boxes.
/// </summary>
public sealed class GuideEntity : Entity
{
    /// <exception cref="ArgumentException">The direction is zero, or a value is not finite.</exception>
    public GuideEntity(Point3d point, Vector3d direction)
    {
        if (!double.IsFinite(point.X + point.Y + point.Z) || !double.IsFinite(direction.Length) || direction.Length == 0)
        {
            throw new ArgumentException("A guide needs a finite point and a direction that is not zero.");
        }
        Point = point;
        Direction = direction.Normalized();
    }

    public Point3d Point { get; }

    /// <summary>Of unit length.</summary>
    public Vector3d Direction { get; }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) => BoundingBox.Empty;
}
