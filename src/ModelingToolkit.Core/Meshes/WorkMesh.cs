using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Meshes;

/// <summary>
/// A mesh being simplified: positions welded, so that triangles that touch share their corners, and triangles that
/// keep the texture coordinates of their corners, which differ across the seams of the texture.
/// </summary>
internal sealed class WorkMesh
{
    public List<Point3d> Positions { get; } = [];

    /// <summary>The corners of the triangles, three by three, as indices into <see cref="Positions"/>.</summary>
    public List<int> Corners { get; } = [];

    /// <summary>The texture coordinates of the corners; null when the mesh has none.</summary>
    public List<Vector2>? Uvs { get; set; }

    /// <summary>The normals of the corners; null when the mesh has none.</summary>
    public List<Vector3>? Normals { get; set; }

    public int TriangleCount => Corners.Count / 3;

    /// <summary>Welds the vertices of a mesh closer than a tolerance, the first of them giving the position.</summary>
    public static WorkMesh Weld(MeshEntity mesh, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var work = new WorkMesh
        {
            Uvs = mesh.TextureCoordinates is null ? null : [],
            Normals = mesh.Normals is null ? null : [],
        };
        var welded = new int[mesh.Vertices.Length];
        double cell = tolerance > 0 ? tolerance : 1e-9;
        var grid = new Dictionary<(long, long, long), List<int>>();
        for (int index = 0; index < mesh.Vertices.Length; index++)
        {
            Point3d point = mesh.Vertices[index];
            (long x, long y, long z) = Cell(point, cell);
            int found = -1;
            for (long dx = -1; dx <= 1 && found < 0; dx++)
            {
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                {
                    for (long dz = -1; dz <= 1 && found < 0; dz++)
                    {
                        if (grid.TryGetValue((x + dx, y + dy, z + dz), out List<int>? near))
                        {
                            foreach (int other in near)
                            {
                                if (work.Positions[other].DistanceTo(point) <= tolerance)
                                {
                                    found = other;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            if (found < 0)
            {
                found = work.Positions.Count;
                work.Positions.Add(point);
                if (!grid.TryGetValue((x, y, z), out List<int>? list))
                {
                    grid[(x, y, z)] = list = [];
                }
                list.Add(found);
            }
            welded[index] = found;
        }
        foreach (Triangle triangle in mesh.Triangles)
        {
            foreach (int corner in (ReadOnlySpan<int>)[triangle.A, triangle.B, triangle.C])
            {
                work.Corners.Add(welded[corner]);
                work.Uvs?.Add(mesh.TextureCoordinates![corner]);
                work.Normals?.Add(mesh.Normals![corner]);
            }
        }
        return work;
    }

    private static (long, long, long) Cell(Point3d point, double size) =>
        ((long)Math.Floor(point.X / size), (long)Math.Floor(point.Y / size), (long)Math.Floor(point.Z / size));

    /// <summary>Keeps the triangles that a test keeps, given their index.</summary>
    public void KeepTriangles(Func<int, bool> keep)
    {
        int kept = 0;
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            if (!keep(triangle))
            {
                continue;
            }
            for (int corner = 0; corner < 3; corner++)
            {
                Corners[3 * kept + corner] = Corners[3 * triangle + corner];
                if (Uvs is not null)
                {
                    Uvs[3 * kept + corner] = Uvs[3 * triangle + corner];
                }
                if (Normals is not null)
                {
                    Normals[3 * kept + corner] = Normals[3 * triangle + corner];
                }
            }
            kept++;
        }
        Corners.RemoveRange(3 * kept, Corners.Count - 3 * kept);
        Uvs?.RemoveRange(3 * kept, Uvs.Count - 3 * kept);
        Normals?.RemoveRange(3 * kept, Normals.Count - 3 * kept);
    }

    /// <summary>Takes out the positions no triangle uses.</summary>
    public void Compact()
    {
        var map = new int[Positions.Count];
        Array.Fill(map, -1);
        var positions = new List<Point3d>();
        for (int corner = 0; corner < Corners.Count; corner++)
        {
            int position = Corners[corner];
            if (map[position] < 0)
            {
                map[position] = positions.Count;
                positions.Add(Positions[position]);
            }
            Corners[corner] = map[position];
        }
        Positions.Clear();
        Positions.AddRange(positions);
    }

    /// <summary>The normal of a triangle, scaled by twice its area.</summary>
    public Vector3d AreaNormal(int triangle)
    {
        Point3d a = Positions[Corners[3 * triangle]], b = Positions[Corners[3 * triangle + 1]], c = Positions[Corners[3 * triangle + 2]];
        return (b - a).Cross(c - a);
    }

    /// <summary>Each edge, its two positions in order, with the triangles it bounds.</summary>
    public Dictionary<(int, int), List<int>> Edges()
    {
        var edges = new Dictionary<(int, int), List<int>>();
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int from = Corners[3 * triangle + corner], to = Corners[3 * triangle + (corner + 1) % 3];
                (int, int) key = from < to ? (from, to) : (to, from);
                if (!edges.TryGetValue(key, out List<int>? list))
                {
                    edges[key] = list = [];
                }
                list.Add(triangle);
            }
        }
        return edges;
    }

    /// <summary>The pieces: for each position, the index of the piece it is in; and how many pieces there are.</summary>
    public (int[] Piece, int Count) Pieces()
    {
        var parent = new int[Positions.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }
        for (int triangle = 0; triangle < TriangleCount; triangle++)
        {
            int a = Find(Corners[3 * triangle]);
            foreach (int corner in (ReadOnlySpan<int>)[Corners[3 * triangle + 1], Corners[3 * triangle + 2]])
            {
                int b = Find(corner);
                if (a != b)
                {
                    parent[b] = a;
                }
            }
        }
        var ids = new Dictionary<int, int>();
        var piece = new int[Positions.Count];
        var used = new bool[Positions.Count];
        foreach (int corner in Corners)
        {
            used[corner] = true;
        }
        for (int i = 0; i < piece.Length; i++)
        {
            if (!used[i])
            {
                piece[i] = -1;
                continue;
            }
            int root = Find(i);
            if (!ids.TryGetValue(root, out int id))
            {
                ids[root] = id = ids.Count;
            }
            piece[i] = id;
        }
        return (piece, ids.Count);
    }
}
