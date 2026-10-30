namespace ModelingToolkit.Core.Geometry;

/// <summary>A point in 3D space; coordinates in millimetres.</summary>
public readonly record struct Point3d(double X, double Y, double Z)
{
    public static Point3d Origin => default;
}
