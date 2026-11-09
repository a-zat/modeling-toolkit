namespace ModelingToolkit.Core.Geometry;

/// <summary>A curve that a drawing adds to a shape: a line, an arc or a circle, in millimetres.</summary>
public abstract record Curve;

/// <summary>A straight segment from <see cref="Start"/> to <see cref="End"/>.</summary>
public sealed record LineCurve(Point3d Start, Point3d End) : Curve;

/// <summary>The arc of a circle from <see cref="Start"/> to <see cref="End"/> through <see cref="Through"/>.</summary>
public sealed record ArcCurve(Point3d Start, Point3d Through, Point3d End) : Curve;

/// <summary>A whole circle, in the plane through <see cref="Centre"/> of normal <see cref="Normal"/>.</summary>
public sealed record CircleCurve(Point3d Centre, Vector3d Normal, double Radius) : Curve;

/// <summary>What kind of curve an edge lies on.</summary>
public enum CurveKind
{
    Line,
    Circle,
    Arc,

    /// <summary>Any other curve, such as a spline.</summary>
    Other,
}

/// <summary>The circle of an edge that is a circle or an arc.</summary>
/// <param name="Normal">Of unit length; an arc turns around it from its start to its end, counterclockwise.</param>
public readonly record struct CircleInfo(Point3d Centre, Vector3d Normal, double Radius);
