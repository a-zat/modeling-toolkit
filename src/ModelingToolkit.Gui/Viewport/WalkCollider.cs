using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// What a person walking through the model bumps into: the triangles of the model, placed, between the height of the
/// knees and above the head, so that thresholds and low steps are walked over. A walk into a wall stops short of it
/// and slides along it.
/// </summary>
internal sealed class WalkCollider
{
    /// <summary>How near the eyes come to what they bump into, in millimetres.</summary>
    public const double Radius = 250;

    // The knees, the hips, the chest and above the head, as parts of the height of the eyes above the floor.
    private static readonly double[] HeightParts = [0.25, 0.5625, 0.875, 1.0625];

    // Rays beside the one from the eyes, as parts of the radius: a thin post between them still stops the walk.
    private static readonly double[] Sides = [0, -0.7, 0.7];

    private readonly List<Part> _parts = [];
    private readonly double _radius;
    private readonly double[] _heights; // from the eyes

    /// <param name="meshes">The meshes of the model, each with its placement in the model space.</param>
    /// <param name="radius">How near the eyes come to what they bump into.</param>
    /// <param name="eyeHeight">Of the eyes above the floor, which the heights of the rays follow.</param>
    public WalkCollider(
        IEnumerable<(MeshEntity Mesh, Transform Placement)> meshes, double radius = Radius,
        double eyeHeight = WalkCamera.EyeHeight)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        _radius = radius;
        _heights = [.. HeightParts.Select(part => (part - 1) * eyeHeight)];
        foreach ((MeshEntity mesh, Transform placement) in meshes)
        {
            Point3d[] vertices = [.. mesh.Vertices.Select(placement.Apply)];
            if (vertices.Length == 0 || mesh.Triangles.Length == 0)
            {
                continue;
            }
            var corners = new Point3d[3 * mesh.Triangles.Length];
            for (int index = 0; index < mesh.Triangles.Length; index++)
            {
                Triangle triangle = mesh.Triangles[index];
                corners[3 * index] = vertices[triangle.A];
                corners[3 * index + 1] = vertices[triangle.B];
                corners[3 * index + 2] = vertices[triangle.C];
            }
            _parts.Add(new Part(
                new Point3d(vertices.Min(p => p.X), vertices.Min(p => p.Y), vertices.Min(p => p.Z)),
                new Point3d(vertices.Max(p => p.X), vertices.Max(p => p.Y), vertices.Max(p => p.Z)),
                corners));
        }
    }

    /// <summary>
    /// The part of a move of the eyes, on the floor, that the model lets through: up to what is in the way, then along
    /// it.
    /// </summary>
    public Vector3d Limit(Point3d eye, Vector3d move)
    {
        if (Free(eye, move) is not { } stop)
        {
            return move;
        }
        // Up to the obstacle, then the rest of the move without what goes into it.
        Vector3d along = stop.Free;
        Vector3d rest = move - along;
        Vector3d slide = rest - stop.Normal * rest.Dot(stop.Normal);
        return Free(eye + along, slide) is { } blocked ? along + blocked.Free : along + slide;
    }

    /// <summary>
    /// Where the eyes stand when put on a point: moved away from what is nearer than the radius, in any direction, which
    /// would stop every move towards it, or the walk altogether when it is all around.
    /// </summary>
    public Point3d Clear(Point3d eye)
    {
        const int Directions = 12;
        for (int round = 0; round < 6; round++)
        {
            Vector3d push = default;
            foreach (double height in _heights)
            {
                for (int k = 0; k < Directions; k++)
                {
                    double angle = 2 * Math.PI * k / Directions;
                    var direction = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
                    if (Cast(eye + new Vector3d(0, 0, height), direction, _radius) is { } hit &&
                        new Vector3d(hit.Normal.X, hit.Normal.Y, 0).Length > 1e-9)
                    {
                        // Away from it, by what lacks to the radius, a little more.
                        push -= direction * (_radius - hit.Distance + 10);
                    }
                }
            }
            if (push.Length < 1)
            {
                break;
            }
            // Every direction counts for a share of the push; a step is never more than the radius.
            Vector3d step = push / (Directions / 4.0 * _heights.Length);
            eye += step.Length > _radius ? step.Normalized() * _radius : step;
        }
        return eye;
    }

    /// <summary>
    /// The height of the ground under a point, as the highest surface that is at or below <paramref name="from"/>, which
    /// is the height of the feet and of the step they climb; null when there is none.
    /// </summary>
    public double? Ground(double x, double y, double from) =>
        Cast(new Point3d(x, y, from), new Vector3d(0, 0, -1), 1e7) is { } hit ? from - hit.Distance : null;

    /// <summary>How far along a ray, of unit direction, the model is first hit, within a distance; null for not at all.</summary>
    public double? Hit(Point3d origin, Vector3d direction, double reach) => Cast(origin, direction, reach)?.Distance;

    // Null when nothing is in the way of the move; else how far it goes, up to the radius from the plane of what
    // stops it, and the horizontal normal of that plane, facing the eyes.
    private (Vector3d Free, Vector3d Normal)? Free(Point3d eye, Vector3d move)
    {
        double length = move.Length;
        if (length < 1e-9)
        {
            return null;
        }
        Vector3d direction = move / length;
        var side = new Vector3d(-direction.Y, direction.X, 0);
        double allowed = length;
        Vector3d? stop = null;
        foreach (double height in _heights)
        {
            foreach (double offset in Sides)
            {
                Point3d origin = eye + new Vector3d(0, 0, height) + side * (offset * _radius);
                if (Cast(origin, direction, length + 2 * _radius) is not { } hit)
                {
                    continue;
                }
                var normal = new Vector3d(hit.Normal.X, hit.Normal.Y, 0);
                if (normal.Length < 1e-9)
                {
                    continue; // a floor or a ceiling, which the walk does not cross
                }
                normal = normal.Normalized();
                if (normal.Dot(direction) > 0)
                {
                    normal = -normal;
                }
                double approach = -normal.Dot(direction);
                if (approach < 1e-6)
                {
                    continue; // along the plane
                }
                // The eyes stay the radius away from the plane through the point hit.
                Point3d point = origin + direction * hit.Distance;
                double distance = (eye.X - point.X) * normal.X + (eye.Y - point.Y) * normal.Y;
                double reach = Math.Max(0, (distance - _radius) / approach);
                if (reach < allowed)
                {
                    allowed = reach;
                    stop = normal;
                }
            }
        }
        return stop is { } found ? (direction * allowed, found) : null;
    }

    // The nearest triangle along a ray, within a distance.
    private (double Distance, Vector3d Normal)? Cast(Point3d origin, Vector3d direction, double reach)
    {
        (double Distance, Vector3d Normal)? nearest = null;
        foreach (Part part in _parts)
        {
            if (!part.Reached(origin, direction, reach))
            {
                continue;
            }
            Point3d[] corners = part.Corners;
            for (int index = 0; index < corners.Length; index += 3)
            {
                if (Intersect(origin, direction, corners[index], corners[index + 1], corners[index + 2]) is { } t &&
                    t < reach)
                {
                    reach = t;
                    nearest = (t, (corners[index + 1] - corners[index]).Cross(corners[index + 2] - corners[index]));
                }
            }
        }
        return nearest;
    }

    // Möller–Trumbore: the distance along the ray to the triangle, either side facing it.
    private static double? Intersect(Point3d origin, Vector3d direction, Point3d a, Point3d b, Point3d c)
    {
        Vector3d ab = b - a, ac = c - a;
        Vector3d p = direction.Cross(ac);
        double determinant = ab.Dot(p);
        if (Math.Abs(determinant) < 1e-12)
        {
            return null;
        }
        double inverse = 1 / determinant;
        Vector3d s = origin - a;
        double u = s.Dot(p) * inverse;
        if (u is < 0 or > 1)
        {
            return null;
        }
        Vector3d q = s.Cross(ab);
        double v = direction.Dot(q) * inverse;
        if (v < 0 || u + v > 1)
        {
            return null;
        }
        double t = ac.Dot(q) * inverse;
        return t >= 0 ? t : null;
    }

    // The triangles of a mesh, three corners each, inside their box.
    private sealed record Part(Point3d Min, Point3d Max, Point3d[] Corners)
    {
        // Whether the ray reaches the box within the distance: the slabs of its sides.
        public bool Reached(Point3d origin, Vector3d direction, double reach)
        {
            double near = 0, far = reach;
            return Slab(origin.X, direction.X, Min.X, Max.X, ref near, ref far) &&
                Slab(origin.Y, direction.Y, Min.Y, Max.Y, ref near, ref far) &&
                Slab(origin.Z, direction.Z, Min.Z, Max.Z, ref near, ref far);
        }

        private static bool Slab(double origin, double direction, double min, double max, ref double near, ref double far)
        {
            if (Math.Abs(direction) < 1e-12)
            {
                return origin >= min && origin <= max;
            }
            double t0 = (min - origin) / direction, t1 = (max - origin) / direction;
            near = Math.Max(near, Math.Min(t0, t1));
            far = Math.Min(far, Math.Max(t0, t1));
            return near <= far;
        }
    }
}
