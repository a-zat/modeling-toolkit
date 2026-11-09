namespace ModelingToolkit.Core.Geometry;

/// <summary>A direction or a displacement in 3D space; components in millimetres, or none for a unit direction.</summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d UnitX => new(1, 0, 0);

    public static Vector3d UnitY => new(0, 1, 0);

    public static Vector3d UnitZ => new(0, 0, 1);

    public double Length => Math.Sqrt(Dot(this));

    public double Dot(Vector3d other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vector3d Cross(Vector3d other) =>
        new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    /// <summary>The vector of unit length in the same direction; zero for the zero vector.</summary>
    public Vector3d Normalized()
    {
        double length = Length;
        return length == 0 ? default : this / length;
    }

    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator -(Vector3d a) => new(-a.X, -a.Y, -a.Z);

    public static Vector3d operator *(Vector3d a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

    public static Vector3d operator *(double factor, Vector3d a) => a * factor;

    public static Vector3d operator /(Vector3d a, double divisor) => new(a.X / divisor, a.Y / divisor, a.Z / divisor);
}
