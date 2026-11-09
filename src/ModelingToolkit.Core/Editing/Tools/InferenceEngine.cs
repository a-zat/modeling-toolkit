using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>Why a point was inferred: what it snaps to.</summary>
public enum InferenceKind
{
    /// <summary>No point: the pointer is on nothing and its ray misses the ground.</summary>
    None,

    /// <summary>On the ground of the edit context, its XY plane.</summary>
    Ground,

    OnFace,
    OnEdge,

    /// <summary>A vertex, the end of edges.</summary>
    Endpoint,

    Midpoint,

    /// <summary>The origin of the edit context.</summary>
    Origin,

    /// <summary>Along the X axis of the edit context, from the point drawn from.</summary>
    AxisX,

    AxisY,
    AxisZ,

    /// <summary>Parallel to the edge last pointed at, from the point drawn from.</summary>
    Parallel,

    /// <summary>Perpendicular to the edge last pointed at, in the plane drawn on.</summary>
    Perpendicular,
}

/// <summary>A point inferred from the pointer, in the model space.</summary>
/// <param name="Plane">The plane drawn on: of the face, or the ground; null elsewhere.</param>
/// <param name="Direction">For the kinds along a direction (axes, parallel, perpendicular), of unit length.</param>
public sealed record Inference(InferenceKind Kind, Point3d Point, Plane? Plane = null, Vector3d? Direction = null)
{
    public static Inference Nothing { get; } = new(InferenceKind.None, default);

    /// <summary>Whether the point is on something, so that drawing to it snaps.</summary>
    public bool IsSnapped => Kind is not (InferenceKind.None or InferenceKind.Ground or InferenceKind.OnFace);
}

/// <summary>
/// Gives each position of the pointer the point that a drawing tool should take: an exact point of the geometry under
/// the pointer (a vertex, the middle of an edge, a point on an edge or a face), the origin, or a point on the ground;
/// and, when drawing from a point, along the axes of the edit context, or parallel or perpendicular to the edge last
/// pointed at, within a few units of the screen of the pointer. Points are in the model space; the axes, the origin and
/// the ground are those of the edit context.
/// </summary>
public sealed class InferenceEngine
{
    private Vector3d? _reference; // the direction of the line last pointed at

    /// <summary>How close the pointer must come to snap, in units of the screen.</summary>
    public double SnapDistance { get; init; } = 8;

    /// <summary>The direction of the line last pointed at, for parallels and perpendiculars; null for none yet.</summary>
    public Vector3d? Reference => _reference;

    /// <summary>Forgets the line last pointed at, as when a tool starts again.</summary>
    public void Reset() => _reference = null;

    /// <param name="placement">The placement of the edit context in the model.</param>
    /// <param name="from">The point drawn from, for the axes, parallels and perpendiculars; null for none.</param>
    /// <param name="locked">A direction the point must lie along from <paramref name="from"/>, as locked by a key.</param>
    public Inference Infer(ToolPick pick, Transform placement, Point3d? from = null, Vector3d? locked = null)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(placement);

        Point3d origin = placement.Apply(Point3d.Origin);
        Vector3d[] axes =
        [
            placement.ApplyVector(Vector3d.UnitX).Normalized(), placement.ApplyVector(Vector3d.UnitY).Normalized(),
            placement.ApplyVector(Vector3d.UnitZ).Normalized(),
        ];
        Plane ground = new(axes[2], axes[2].Dot(origin - Point3d.Origin));

        if (from is { } start && locked is { } direction && pick.Ray is { } lockedRay)
        {
            Vector3d unit = direction.Normalized();
            (Point3d along, _) = ClosestOnLine(lockedRay, start, unit);
            return new Inference(KindOf(unit, axes), along, null, unit);
        }

        Inference found = OnGeometry(pick) ?? OnGround(pick, ground);
        if (found.Kind is InferenceKind.Endpoint or InferenceKind.Midpoint)
        {
            return found;
        }

        if (pick.Ray is { } ray && found.Kind != InferenceKind.None &&
            found.Point.DistanceTo(origin) < SnapDistance * ray.UnitSizeAt(origin))
        {
            return new Inference(InferenceKind.Origin, origin, found.Plane);
        }

        if (from is { } drawnFrom && pick.Ray is { } pointer && found.Kind != InferenceKind.OnEdge)
        {
            Plane plane = found.Plane ?? ground;
            if (Along(pointer, drawnFrom, axes, plane) is { } aligned)
            {
                return aligned with { Plane = found.Plane };
            }
        }
        return found;
    }

    // Along an axis, the reference line or its perpendicular in the plane, from a point, if the ray passes close.
    private Inference? Along(PointerRay ray, Point3d from, Vector3d[] axes, Plane plane)
    {
        var directions = new List<(InferenceKind Kind, Vector3d Direction)>
        {
            (InferenceKind.AxisX, axes[0]), (InferenceKind.AxisY, axes[1]), (InferenceKind.AxisZ, axes[2]),
        };
        if (_reference is { } reference && !axes.Any(axis => Parallel(axis, reference)))
        {
            directions.Add((InferenceKind.Parallel, reference));
        }
        if (_reference is { } line)
        {
            Vector3d perpendicular = plane.Normal.Cross(line).Normalized();
            if (perpendicular.Length > 0 && !axes.Any(axis => Parallel(axis, perpendicular)))
            {
                directions.Add((InferenceKind.Perpendicular, perpendicular));
            }
        }

        Inference? best = null;
        double bestDistance = double.MaxValue;
        foreach ((InferenceKind kind, Vector3d direction) in directions)
        {
            (Point3d point, double distance) = ClosestOnLine(ray, from, direction);
            if (point.DistanceTo(from) < 1e-9)
            {
                continue;
            }
            double units = distance / ray.UnitSizeAt(point);
            if (units < SnapDistance && units < bestDistance)
            {
                best = new Inference(kind, point, null, direction);
                bestDistance = units;
            }
        }
        return best;
    }

    private Inference? OnGeometry(ToolPick pick)
    {
        if (pick.Element is not { } element)
        {
            return null;
        }

        BrepEntity entity = element.Entity;
        Transform placement = PlacementOf(element.Path);
        double unit = pick.Ray?.UnitSizeAt(pick.Point) ?? 0;
        switch (element.Kind)
        {
            case ElementKind.Vertex:
                return new Inference(InferenceKind.Endpoint,
                    placement.Apply(entity.Kernel.GetVertexPoint(entity.Shape, element.Index)));

            case ElementKind.Edge:
            {
                EdgeInfo edge = entity.Kernel.GetEdgeInfo(entity.Shape, element.Index);
                Point3d start = placement.Apply(edge.Start), end = placement.Apply(edge.End);
                Point3d middle = placement.Apply(edge.Middle);
                if (edge.Kind == CurveKind.Line)
                {
                    _reference = (end - start).Normalized();
                }
                if (!edge.IsClosed && pick.Point.DistanceTo(middle) < SnapDistance * unit)
                {
                    return new Inference(InferenceKind.Midpoint, middle);
                }
                if (edge.Circle is { } circle)
                {
                    Point3d centre = placement.Apply(circle.Centre);
                    Vector3d normal = placement.ApplyVector(circle.Normal).Normalized();
                    Vector3d radial = pick.Point - centre;
                    radial = (radial - normal * radial.Dot(normal)).Normalized();
                    double radius = (placement.Apply(circle.Centre + Perpendicular(circle.Normal) * circle.Radius) -
                                     centre).Length;
                    return radial.Length == 0
                        ? new Inference(InferenceKind.OnEdge, pick.Point)
                        : new Inference(InferenceKind.OnEdge, centre + radial * radius);
                }
                if (edge.Kind == CurveKind.Line)
                {
                    Vector3d along = end - start;
                    double t = Math.Clamp((pick.Point - start).Dot(along) / along.Dot(along), 0, 1);
                    if (pick.Ray is { } ray)
                    {
                        (Point3d closest, _) = ClosestOnLine(ray, start, along.Normalized());
                        t = Math.Clamp((closest - start).Dot(along) / along.Dot(along), 0, 1);
                    }
                    return new Inference(InferenceKind.OnEdge, start + along * t);
                }
                return new Inference(InferenceKind.OnEdge, pick.Point);
            }

            default:
            {
                FaceInfo face = entity.Kernel.GetFaceInfo(entity.Shape, element.Index);
                if (face.Plane is not { } local)
                {
                    return new Inference(InferenceKind.OnFace, pick.Point);
                }
                Point3d onPlane = Point3d.Origin + local.Normal * local.Offset;
                Point3d placed = placement.Apply(onPlane);
                Vector3d normal = placement.ApplyVector(local.Normal).Normalized();
                var plane = new Plane(normal, normal.Dot(placed - Point3d.Origin));
                Point3d point = pick.Ray is { } ray && Intersect(ray, plane) is { } hit ? hit : pick.Point;
                return new Inference(InferenceKind.OnFace, point, plane);
            }
        }
    }

    private static Inference OnGround(ToolPick pick, Plane ground)
    {
        if (pick.Ray is { } ray && Intersect(ray, ground) is { } hit)
        {
            return new Inference(InferenceKind.Ground, hit, ground);
        }
        return Inference.Nothing;
    }

    /// <summary>The placement in the model of the entity at the end of a path: the references along it, composed.</summary>
    public static Transform PlacementOf(IReadOnlyList<Entity> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Transform placement = Transform.Identity;
        foreach (Entity entity in path)
        {
            if (entity is BlockReference reference)
            {
                placement *= reference.Transform;
            }
        }
        return placement;
    }

    /// <summary>Where a ray meets a plane, ahead of its origin; null when it does not.</summary>
    public static Point3d? Intersect(PointerRay ray, Plane plane)
    {
        ArgumentNullException.ThrowIfNull(ray);
        double facing = plane.Normal.Dot(ray.Direction);
        if (Math.Abs(facing) < 1e-12)
        {
            return null;
        }
        double distance = (plane.Offset - plane.Normal.Dot(ray.Origin - Point3d.Origin)) / facing;
        return distance < 0 ? null : ray.At(distance);
    }

    // The point of the line through a point along a direction of unit length closest to the ray, and its distance from
    // the ray.
    private static (Point3d Point, double Distance) ClosestOnLine(PointerRay ray, Point3d point, Vector3d direction)
    {
        Vector3d between = point - ray.Origin;
        double b = direction.Dot(ray.Direction);
        double denominator = 1 - b * b;
        double s = denominator < 1e-12
            ? 0
            : (b * between.Dot(ray.Direction) - between.Dot(direction)) / denominator;
        Point3d onLine = point + direction * s;
        double t = Math.Max((onLine - ray.Origin).Dot(ray.Direction), 0);
        return (onLine, onLine.DistanceTo(ray.At(t)));
    }

    private static InferenceKind KindOf(Vector3d direction, Vector3d[] axes) =>
        Parallel(direction, axes[0]) ? InferenceKind.AxisX :
        Parallel(direction, axes[1]) ? InferenceKind.AxisY :
        Parallel(direction, axes[2]) ? InferenceKind.AxisZ : InferenceKind.Parallel;

    private static bool Parallel(Vector3d a, Vector3d b) => Math.Abs(Math.Abs(a.Normalized().Dot(b.Normalized())) - 1) < 1e-9;

    private static Vector3d Perpendicular(Vector3d normal)
    {
        Vector3d other = Math.Abs(normal.X) < 0.9 ? Vector3d.UnitX : Vector3d.UnitY;
        return normal.Cross(other).Normalized();
    }
}
