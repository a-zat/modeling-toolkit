namespace ModelingToolkit.Core.Geometry;

/// <summary>
/// The ray of the pointer into the 3D view, from a point in front of the camera along a direction of unit length, with
/// the size of a unit of the screen (a device-independent pixel) along it, to snap within a few units of the pointer.
/// </summary>
/// <param name="UnitSize">The size of a unit of the screen at the origin, in millimetres.</param>
/// <param name="UnitGrowth">How much it grows per millimetre along the ray: 0 in an orthographic view.</param>
public sealed record PointerRay(Point3d Origin, Vector3d Direction, double UnitSize, double UnitGrowth)
{
    /// <summary>The size of a unit of the screen at the depth of a point, in millimetres.</summary>
    public double UnitSizeAt(Point3d point)
    {
        double along = (point.X - Origin.X) * Direction.X + (point.Y - Origin.Y) * Direction.Y +
                       (point.Z - Origin.Z) * Direction.Z;
        return UnitSize + Math.Max(along, 0) * UnitGrowth;
    }

    /// <summary>The point of the ray at a distance along it.</summary>
    public Point3d At(double distance) => new(
        Origin.X + Direction.X * distance, Origin.Y + Direction.Y * distance, Origin.Z + Direction.Z * distance);

    /// <summary>The transformed ray, as in another coordinate system, which must not scale (units keep their size).</summary>
    public PointerRay Transformed(Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Point3d origin = transform.Apply(Origin);
        Point3d ahead = transform.Apply(At(1));
        return this with { Origin = origin, Direction = new Vector3d(ahead.X - origin.X, ahead.Y - origin.Y, ahead.Z - origin.Z) };
    }
}
