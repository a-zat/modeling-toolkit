namespace ModelingToolkit.Core.Geometry;

/// <summary>
/// Affine transformation of 3D space: a linear part (rotation, scale, shear) followed by a translation.
/// </summary>
/// <remarks>
/// The values are the first three rows of a 4×4 matrix applied to column vectors, p' = M·p: <c>Mij</c> is the value at
/// row i, column j, and the fourth column (<c>M03</c>, <c>M13</c>, <c>M23</c>) is the translation.
/// </remarks>
public sealed record Transform(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23)
{
    public static Transform Identity { get; } = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0);

    public bool IsIdentity => Equals(Identity);

    public static Transform Translation(double x, double y, double z) => new(
        1, 0, 0, x,
        0, 1, 0, y,
        0, 0, 1, z);

    public Point3d Apply(Point3d point) => new(
        M00 * point.X + M01 * point.Y + M02 * point.Z + M03,
        M10 * point.X + M11 * point.Y + M12 * point.Z + M13,
        M20 * point.X + M21 * point.Y + M22 * point.Z + M23);

    /// <summary>The linear part applied to a vector, without the translation.</summary>
    public Vector3d ApplyVector(Vector3d vector) => new(
        M00 * vector.X + M01 * vector.Y + M02 * vector.Z,
        M10 * vector.X + M11 * vector.Y + M12 * vector.Z,
        M20 * vector.X + M21 * vector.Y + M22 * vector.Z);

    /// <summary>The transformation that undoes this one.</summary>
    /// <exception cref="InvalidOperationException">The transformation is not invertible, as a scale by 0.</exception>
    public Transform Inverse()
    {
        double c00 = M11 * M22 - M12 * M21, c01 = M02 * M21 - M01 * M22, c02 = M01 * M12 - M02 * M11;
        double c10 = M12 * M20 - M10 * M22, c11 = M00 * M22 - M02 * M20, c12 = M02 * M10 - M00 * M12;
        double c20 = M10 * M21 - M11 * M20, c21 = M01 * M20 - M00 * M21, c22 = M00 * M11 - M01 * M10;
        double determinant = M00 * c00 + M01 * c10 + M02 * c20;
        if (Math.Abs(determinant) < 1e-15)
        {
            throw new InvalidOperationException("The transformation is not invertible.");
        }

        double i00 = c00 / determinant, i01 = c01 / determinant, i02 = c02 / determinant;
        double i10 = c10 / determinant, i11 = c11 / determinant, i12 = c12 / determinant;
        double i20 = c20 / determinant, i21 = c21 / determinant, i22 = c22 / determinant;
        return new Transform(
            i00, i01, i02, -(i00 * M03 + i01 * M13 + i02 * M23),
            i10, i11, i12, -(i10 * M03 + i11 * M13 + i12 * M23),
            i20, i21, i22, -(i20 * M03 + i21 * M13 + i22 * M23));
    }

    /// <summary>Composition: <c>(a * b).Apply(p)</c> equals <c>a.Apply(b.Apply(p))</c>, so <paramref name="b"/> is applied first.</summary>
    public static Transform operator *(Transform a, Transform b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
        a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23);
}
