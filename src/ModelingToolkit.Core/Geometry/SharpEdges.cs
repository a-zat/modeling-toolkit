using System.Numerics;

namespace ModelingToolkit.Core.Geometry;

/// <summary>
/// The edges of a triangle mesh that outline its shape: where its faces meet at an angle, as the edges of a box, and
/// its borders. The edges inside flat or gently curved parts, such as the diagonals of the faces of a box, are left
/// out.
/// </summary>
public static class SharpEdges
{
    /// <summary>The angle between the faces of a sharp edge, below which a surface is seen as smooth: 30°.</summary>
    public const double DefaultAngle = Math.PI / 6;

    /// <summary>
    /// The sharp edges of a mesh, each a polyline of two points: those between triangles whose normals make more than
    /// <paramref name="angle"/>, those of a single triangle (a border) and those shared by more than two. Vertices at
    /// the same position count as one, as the meshes of files repeat them to give each face its own normals.
    /// </summary>
    public static Polylines Of(ReadOnlySpan<Point3d> vertices, ReadOnlySpan<Triangle> triangles,
        double angle = DefaultAngle)
    {
        // The same position is one vertex, whatever the index the triangles give it.
        var positions = new Dictionary<Point3d, int>();
        var welded = new int[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
        {
            if (!positions.TryGetValue(vertices[index], out welded[index]))
            {
                welded[index] = positions[vertices[index]] = index;
            }
        }

        // Each edge with the triangles it bounds: the first one, the last one and how many.
        var edges = new Dictionary<(int, int), (int First, int Last, int Count)>();
        var normals = new Vector3[triangles.Length];
        for (int index = 0; index < triangles.Length; index++)
        {
            Triangle triangle = triangles[index];
            int a = welded[triangle.A], b = welded[triangle.B], c = welded[triangle.C];
            normals[index] = Normal(vertices[a], vertices[b], vertices[c]);
            if (normals[index] == Vector3.Zero)
            {
                continue; // without area, as with two corners at the same place: no face, no edges
            }
            foreach ((int from, int to) in (ReadOnlySpan<(int, int)>)[(a, b), (b, c), (c, a)])
            {
                (int, int) key = from < to ? (from, to) : (to, from);
                edges[key] = edges.TryGetValue(key, out var found)
                    ? (found.First, index, found.Count + 1)
                    : (index, index, 1);
            }
        }

        double smooth = Math.Cos(angle);
        var points = new List<Point3d>();
        var firstPoints = new List<int> { 0 };
        foreach (((int from, int to), (int first, int last, int count)) in edges)
        {
            if (count == 2 && Vector3.Dot(normals[first], normals[last]) >= smooth)
            {
                continue;
            }
            points.Add(vertices[from]);
            points.Add(vertices[to]);
            firstPoints.Add(points.Count);
        }
        return new Polylines([.. points], [.. firstPoints]);
    }

    // The unit normal of a triangle, from its corners in counter-clockwise order; zero for a triangle without area.
    private static Vector3 Normal(Point3d a, Point3d b, Point3d c)
    {
        var u = new Vector3((float)(b.X - a.X), (float)(b.Y - a.Y), (float)(b.Z - a.Z));
        var v = new Vector3((float)(c.X - a.X), (float)(c.Y - a.Y), (float)(c.Z - a.Z));
        Vector3 normal = Vector3.Cross(u, v);
        float length = normal.Length();
        return length > 0 ? normal / length : Vector3.Zero;
    }
}
