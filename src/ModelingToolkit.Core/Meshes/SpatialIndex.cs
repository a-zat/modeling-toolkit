using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Meshes;

/// <summary>The triangles of a mesh in the cells of a grid, to find how far a point is from its surface.</summary>
internal sealed class TriangleIndex
{
    private readonly MeshEntity _mesh;
    private readonly Dictionary<(long, long, long), List<int>> _cells = [];
    private readonly double _size;

    public TriangleIndex(MeshEntity mesh)
    {
        _mesh = mesh;
        BoundingBox box = mesh.GetBoundingBox();
        double diagonal = box.IsEmpty ? 1 : Math.Max((box.Max - box.Min).Length, 1e-6);
        _size = diagonal / Math.Max(1, Math.Sqrt(mesh.Triangles.Length));
        for (int index = 0; index < mesh.Triangles.Length; index++)
        {
            Triangle t = mesh.Triangles[index];
            BoundingBox bounds = BoundingBox.Empty.Include(mesh.Vertices[t.A]).Include(mesh.Vertices[t.B]).Include(mesh.Vertices[t.C]);
            (long x0, long y0, long z0) = Cell(bounds.Min);
            (long x1, long y1, long z1) = Cell(bounds.Max);
            for (long x = x0; x <= x1; x++)
            {
                for (long y = y0; y <= y1; y++)
                {
                    for (long z = z0; z <= z1; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out List<int>? list))
                        {
                            _cells[(x, y, z)] = list = [];
                        }
                        list.Add(index);
                    }
                }
            }
        }
    }

    private (long, long, long) Cell(Point3d point) =>
        ((long)Math.Floor(point.X / _size), (long)Math.Floor(point.Y / _size), (long)Math.Floor(point.Z / _size));

    /// <summary>The distance from a point to the nearest triangle.</summary>
    public double Distance(Point3d point)
    {
        (long x, long y, long z) = Cell(point);
        double best = double.MaxValue;
        for (int ring = 0; ring <= 16; ring = ring == 0 ? 1 : ring * 2)
        {
            var seen = new HashSet<int>();
            for (long dx = -ring; dx <= ring; dx++)
            {
                for (long dy = -ring; dy <= ring; dy++)
                {
                    for (long dz = -ring; dz <= ring; dz++)
                    {
                        if (_cells.TryGetValue((x + dx, y + dy, z + dz), out List<int>? list))
                        {
                            foreach (int index in list)
                            {
                                if (seen.Add(index))
                                {
                                    Triangle t = _mesh.Triangles[index];
                                    best = Math.Min(best, ToTriangle(point,
                                        _mesh.Vertices[t.A], _mesh.Vertices[t.B], _mesh.Vertices[t.C]));
                                }
                            }
                        }
                    }
                }
            }
            // Within this ring of cells, nothing outside it is nearer.
            if (best <= ring * _size)
            {
                return best;
            }
            if (_cells.Count == 0)
            {
                return 0;
            }
        }
        // Far from the surface: all the triangles.
        foreach (Triangle t in _mesh.Triangles)
        {
            best = Math.Min(best, ToTriangle(point, _mesh.Vertices[t.A], _mesh.Vertices[t.B], _mesh.Vertices[t.C]));
        }
        return best == double.MaxValue ? 0 : best;
    }

    // The distance from a point to a triangle (Ericson, Real-Time Collision Detection, 5.1.5).
    internal static double ToTriangle(Point3d p, Point3d a, Point3d b, Point3d c)
    {
        Vector3d ab = b - a, ac = c - a, ap = p - a;
        double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return p.DistanceTo(a);
        }
        Vector3d bp = p - b;
        double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return p.DistanceTo(b);
        }
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return p.DistanceTo(a + ab * (d1 / (d1 - d3)));
        }
        Vector3d cp = p - c;
        double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return p.DistanceTo(c);
        }
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return p.DistanceTo(a + ac * (d2 / (d2 - d6)));
        }
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            return p.DistanceTo(b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6))));
        }
        double denominator = 1 / (va + vb + vc);
        return p.DistanceTo(a + ab * (vb * denominator) + ac * (vc * denominator));
    }
}
