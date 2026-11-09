namespace ModelingToolkit.Core.Geometry;

/// <summary>A point in 3D space; coordinates in millimetres.</summary>
public readonly record struct Point3d(double X, double Y, double Z)
{
    public static Point3d Origin => default;

    public double DistanceTo(Point3d other) => (this - other).Length;

    public static Vector3d operator -(Point3d a, Point3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Point3d operator +(Point3d point, Vector3d vector) =>
        new(point.X + vector.X, point.Y + vector.Y, point.Z + vector.Z);

    public static Point3d operator -(Point3d point, Vector3d vector) =>
        new(point.X - vector.X, point.Y - vector.Y, point.Z - vector.Z);
}
