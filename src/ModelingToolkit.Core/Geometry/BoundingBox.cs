namespace ModelingToolkit.Core.Geometry;

/// <summary>Axis-aligned bounding box.</summary>
/// <remarks>
/// Start from <see cref="Empty"/> to accumulate points: the default value is the box that contains only the origin.
/// </remarks>
public readonly record struct BoundingBox(Point3d Min, Point3d Max)
{
    public static BoundingBox Empty { get; } = new(
        new Point3d(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Point3d(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public BoundingBox Include(Point3d point) => new(
        new Point3d(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z)),
        new Point3d(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z)));

    public BoundingBox Union(BoundingBox other) => other.IsEmpty ? this : Include(other.Min).Include(other.Max);

    /// <summary>The box containing this box after the transformation: it can be larger than the transformed geometry.</summary>
    public BoundingBox Transform(Transform transform)
    {
        if (IsEmpty)
        {
            return this;
        }

        BoundingBox result = Empty;
        foreach (double x in (ReadOnlySpan<double>)[Min.X, Max.X])
        {
            foreach (double y in (ReadOnlySpan<double>)[Min.Y, Max.Y])
            {
                foreach (double z in (ReadOnlySpan<double>)[Min.Z, Max.Z])
                {
                    result = result.Include(transform.Apply(new Point3d(x, y, z)));
                }
            }
        }
        return result;
    }
}
