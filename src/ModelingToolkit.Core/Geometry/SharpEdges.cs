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
    /// the same position count as one, as the meshes of files repeat them to give each face its own normals. With the
    /// normals of the vertices, an edge is sharp only where they differ on its two sides too: a surface that its
    /// normals shade as smooth has no edges, however rough its triangles.
    /// </summary>
    public static Polylines Of(ReadOnlySpan<Point3d> vertices, ReadOnlySpan<Triangle> triangles,
        ReadOnlySpan<Vector3> normals = default, double angle = DefaultAngle)
    {
        bool shaded = normals.Length == vertices.Length && normals.Length > 0;
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
        var corners = new int[3 * triangles.Length]; // as the triangles give them, before welding
        var faceNormals = new Vector3[triangles.Length];
        for (int index = 0; index < triangles.Length; index++)
        {
            Triangle triangle = triangles[index];
            corners[3 * index] = triangle.A;
            corners[3 * index + 1] = triangle.B;
            corners[3 * index + 2] = triangle.C;
            int a = welded[triangle.A], b = welded[triangle.B], c = welded[triangle.C];
            faceNormals[index] = Normal(vertices[a], vertices[b], vertices[c]);
            if (faceNormals[index] == Vector3.Zero)
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
        Vector3[] vertexNormals = shaded ? normals.ToArray() : [];

        // Whether the normals at a position differ in two triangles.
        bool Split(int position, int first, int second) =>
            Vector3.Dot(vertexNormals[CornerAt(first, position)], vertexNormals[CornerAt(second, position)]) < 0.999f;

        int CornerAt(int triangle, int position)
        {
            for (int corner = 3 * triangle; corner < 3 * triangle + 3; corner++)
            {
                if (welded[corners[corner]] == position)
                {
                    return corners[corner];
                }
            }
            return corners[3 * triangle];
        }
        var points = new List<Point3d>();
        var firstPoints = new List<int> { 0 };
        foreach (((int from, int to), (int first, int last, int count)) in edges)
        {
            if (count == 2 && (Vector3.Dot(faceNormals[first], faceNormals[last]) >= smooth ||
                               shaded && !(Split(from, first, last) && Split(to, first, last))))
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
