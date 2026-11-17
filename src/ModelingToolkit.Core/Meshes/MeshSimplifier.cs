using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Meshes;

/// <summary>What a mesh is made of, for comparing it before and after simplifying it.</summary>
/// <param name="Edges">The edges drawn over its faces, its sharp ones (<see cref="SharpEdges"/>).</param>
public readonly record struct MeshMeasures(int Triangles, int Vertices, int Pieces, int Edges)
{
    public static MeshMeasures operator +(MeshMeasures a, MeshMeasures b) =>
        new(a.Triangles + b.Triangles, a.Vertices + b.Vertices, a.Pieces + b.Pieces, a.Edges + b.Edges);

    public static MeshMeasures Of(MeshEntity mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        WorkMesh work = WorkMesh.Weld(mesh, 0);
        return new MeshMeasures(mesh.Triangles.Length, mesh.Vertices.Length, work.Pieces().Count,
            SharpEdges.Of(mesh.Vertices, mesh.Triangles, mesh.Normals).Count);
    }
}

/// <summary>A mesh simplified: the result, null when nothing is left, and how it compares with the mesh it was.</summary>
/// <param name="MaxDeviation">The furthest a point of either is from the surface of the other, in millimetres.</param>
/// <param name="MeanDeviation">The mean of those distances.</param>
public sealed record SimplifiedMesh(
    MeshEntity Original, MeshEntity? Result, MeshMeasures Before, MeshMeasures After, double MaxDeviation,
    double MeanDeviation);

/// <summary>Simplifies triangle meshes, as <see cref="SimplifyOptions"/> tell.</summary>
public static class MeshSimplifier
{
    /// <summary>Simplifies a mesh; the result has its name and its material.</summary>
    public static SimplifiedMesh Simplify(
        MeshEntity mesh, SimplifyOptions options, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(options);
        MeshEntity? result = SimplifyGeometry(mesh, options, cancellation);
        if (result is not null)
        {
            result.Name = mesh.Name;
            result.Material = mesh.Material;
        }
        MeshMeasures before = MeshMeasures.Of(mesh);
        MeshMeasures after = result is null ? default : MeshMeasures.Of(result);
        (double max, double mean) = result is null ? (0, 0) : Deviation(mesh, result, cancellation);
        return new SimplifiedMesh(mesh, result, before, after, max, mean);
    }

    private static MeshEntity? SimplifyGeometry(MeshEntity mesh, SimplifyOptions options, CancellationToken cancellation)
    {
        WorkMesh work = WorkMesh.Weld(mesh, Math.Max(options.WeldTolerance, 0));
        if (options.RemoveDegenerate)
        {
            RemoveDegenerate(work);
        }
        if (options.SoftenAngle > 0 && options.Normals == NormalsMode.Keep && work.Normals is not null)
        {
            Soften(work, options.SoftenAngle);
        }
        if (options.MinPieceSize > 0)
        {
            RemoveSmallPieces(work, options.MinPieceSize);
        }
        work.Compact();
        if (work.TriangleCount == 0)
        {
            return null;
        }
        cancellation.ThrowIfCancellationRequested();
        if (options.SmoothIterations > 0)
        {
            Smooth(work, options.SmoothIterations, options.SmoothMaxMove, options.SmoothKeepAngle);
        }
        cancellation.ThrowIfCancellationRequested();
        if (options.KeepPercent < 100)
        {
            int target = (int)Math.Ceiling(work.TriangleCount * Math.Clamp(options.KeepPercent, 0, 100) / 100);
            new QuadricDecimator(work, options.KeepBorders, options.KeepTextureSeams).Reduce(target, options.MaxError, cancellation);
        }
        if (work.TriangleCount == 0)
        {
            return null;
        }
        return options.Normals == NormalsMode.Recompute || work.Normals is null
            ? Build(work, CreaseNormals(work, options.CreaseAngle))
            : Build(work, options.SmoothIterations > 0 || options.KeepPercent < 100
                ? Checked(work, [.. work.Normals])
                : [.. work.Normals]);
    }

    // Triangles without area, and repeated ones: the same corners in the same turn.
    private static void RemoveDegenerate(WorkMesh work)
    {
        var seen = new HashSet<(int, int, int)>();
        double scale = 0;
        foreach (Point3d point in work.Positions)
        {
            scale = Math.Max(scale, Math.Abs(point.X) + Math.Abs(point.Y) + Math.Abs(point.Z));
        }
        double tiny = 1e-12 * Math.Max(scale * scale, 1);
        work.KeepTriangles(triangle =>
        {
            int a = work.Corners[3 * triangle], b = work.Corners[3 * triangle + 1], c = work.Corners[3 * triangle + 2];
            if (a == b || b == c || a == c || work.AreaNormal(triangle).Length <= tiny)
            {
                return false;
            }
            // The same triangle, whatever the corner it starts from.
            (int, int, int) key = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
            return seen.Add(key);
        });
    }

    // The normals of the corners of each position closer than an angle, as one: their mean, grouped around the first.
    private static void Soften(WorkMesh work, double angle)
    {
        List<Vector3> normals = work.Normals!;
        float close = (float)Math.Cos(angle * Math.PI / 180);
        var corners = new List<int>[work.Positions.Count];
        for (int corner = 0; corner < work.Corners.Count; corner++)
        {
            (corners[work.Corners[corner]] ??= []).Add(corner);
        }
        var groups = new List<(Vector3 First, Vector3 Sum, List<int> Corners)>();
        foreach (List<int>? around in corners)
        {
            if (around is null || around.Count < 2)
            {
                continue;
            }
            groups.Clear();
            foreach (int corner in around)
            {
                Vector3 normal = normals[corner];
                int found = groups.FindIndex(group => Vector3.Dot(group.First, normal) >= close);
                if (found < 0)
                {
                    groups.Add((normal, normal, [corner]));
                }
                else
                {
                    (Vector3 first, Vector3 sum, List<int> members) = groups[found];
                    members.Add(corner);
                    groups[found] = (first, sum + normal, members);
                }
            }
            foreach ((_, Vector3 sum, List<int> members) in groups)
            {
                Vector3 mean = sum.LengthSquared() > 0 ? Vector3.Normalize(sum) : normals[members[0]];
                foreach (int corner in members)
                {
                    normals[corner] = mean;
                }
            }
        }
    }

    private static void RemoveSmallPieces(WorkMesh work, double size)
    {
        (int[] piece, int count) = work.Pieces();
        var boxes = new BoundingBox[count];
        Array.Fill(boxes, BoundingBox.Empty);
        for (int position = 0; position < piece.Length; position++)
        {
            if (piece[position] >= 0)
            {
                boxes[piece[position]] = boxes[piece[position]].Include(work.Positions[position]);
            }
        }
        work.KeepTriangles(triangle =>
        {
            BoundingBox box = boxes[piece[work.Corners[3 * triangle]]];
            return (box.Max - box.Min).Length >= size;
        });
    }

    // Taubin's smoothing, which keeps the volume, each vertex no further than a distance from where it was; the borders,
    // and the vertices of edges sharper than an angle, stay.
    private static void Smooth(WorkMesh work, int iterations, double maxMove, double keepAngle)
    {
        int count = work.Positions.Count;
        var neighbours = new HashSet<int>[count];
        for (int i = 0; i < count; i++)
        {
            neighbours[i] = [];
        }
        for (int triangle = 0; triangle < work.TriangleCount; triangle++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int a = work.Corners[3 * triangle + corner], b = work.Corners[3 * triangle + (corner + 1) % 3];
                neighbours[a].Add(b);
                neighbours[b].Add(a);
            }
        }
        var fixedVertices = new bool[count];
        double sharp = keepAngle > 0 ? Math.Cos(keepAngle * Math.PI / 180) : double.NegativeInfinity;
        foreach (((int a, int b), List<int> around) in work.Edges())
        {
            bool keep = around.Count != 2;
            if (!keep && keepAngle > 0)
            {
                Vector3d n0 = work.AreaNormal(around[0]), n1 = work.AreaNormal(around[1]);
                keep = n0.Dot(n1) < sharp * n0.Length * n1.Length;
            }
            if (keep)
            {
                fixedVertices[a] = fixedVertices[b] = true;
            }
        }
        Point3d[] original = [.. work.Positions];
        int[][] around2 = [.. neighbours.Select(set => set.ToArray())];
        var next = new Point3d[count];
        for (int pass = 0; pass < iterations; pass++)
        {
            foreach (double factor in (ReadOnlySpan<double>)[0.5, -0.53])
            {
                for (int i = 0; i < count; i++)
                {
                    Point3d p = work.Positions[i];
                    if (fixedVertices[i] || around2[i].Length == 0)
                    {
                        next[i] = p;
                        continue;
                    }
                    Vector3d sum = default;
                    foreach (int other in around2[i])
                    {
                        sum += work.Positions[other] - Point3d.Origin;
                    }
                    Point3d average = Point3d.Origin + sum / around2[i].Length;
                    Point3d moved = p + (average - p) * factor;
                    Vector3d away = moved - original[i];
                    next[i] = away.Length > maxMove && maxMove > 0 ? original[i] + away * (maxMove / away.Length) : moved;
                }
                for (int i = 0; i < count; i++)
                {
                    work.Positions[i] = next[i];
                }
            }
        }
    }

    // The normal of each corner: the mean of those of the triangles around its position that meet its own triangle at
    // less than the crease angle, weighted by their areas.
    private static Vector3[] CreaseNormals(WorkMesh work, double creaseAngle)
    {
        double smooth = Math.Cos(Math.Clamp(creaseAngle, 0, 180) * Math.PI / 180);
        var faces = new Vector3d[work.TriangleCount];
        var around = new List<int>[work.Positions.Count];
        for (int i = 0; i < around.Length; i++)
        {
            around[i] = [];
        }
        for (int triangle = 0; triangle < work.TriangleCount; triangle++)
        {
            faces[triangle] = work.AreaNormal(triangle);
            for (int corner = 0; corner < 3; corner++)
            {
                around[work.Corners[3 * triangle + corner]].Add(triangle);
            }
        }
        var normals = new Vector3[work.Corners.Count];
        for (int triangle = 0; triangle < work.TriangleCount; triangle++)
        {
            Vector3d own = faces[triangle];
            double ownLength = own.Length;
            for (int corner = 0; corner < 3; corner++)
            {
                Vector3d sum = default;
                foreach (int other in around[work.Corners[3 * triangle + corner]])
                {
                    Vector3d face = faces[other];
                    if (other == triangle || face.Dot(own) >= smooth * face.Length * ownLength)
                    {
                        sum += face;
                    }
                }
                normals[3 * triangle + corner] = ToUnit(sum.Length > 0 ? sum : own);
            }
        }
        return normals;
    }

    // The normals kept, but those that the changes turned away from their triangles, as smoothing and reducing may, made
    // again from the triangles around.
    private static Vector3[] Checked(WorkMesh work, Vector3[] normals)
    {
        Vector3[]? remade = null;
        // Even as they come, the normals of rough meshes stray from their triangles by 60° or more: only those turned
        // about away from them.
        float away = (float)Math.Cos(80 * Math.PI / 180);
        for (int triangle = 0; triangle < work.TriangleCount; triangle++)
        {
            Vector3 face = ToUnit(work.AreaNormal(triangle));
            for (int corner = 3 * triangle; corner < 3 * triangle + 3; corner++)
            {
                if (Vector3.Dot(normals[corner], face) < away)
                {
                    remade ??= CreaseNormals(work, 60);
                    normals[corner] = remade[corner];
                }
            }
        }
        return normals;
    }

    private static Vector3 ToUnit(Vector3d vector)
    {
        double length = vector.Length;
        return length > 0
            ? new Vector3((float)(vector.X / length), (float)(vector.Y / length), (float)(vector.Z / length))
            : Vector3.UnitZ;
    }

    // A mesh of the work, one vertex for each position, normal and texture coordinate met together.
    private static MeshEntity Build(WorkMesh work, Vector3[] normals)
    {
        var vertices = new List<Point3d>();
        var vertexNormals = new List<Vector3>();
        List<Vector2>? uvs = work.Uvs is null ? null : [];
        var indices = new Dictionary<(int, Vector3, Vector2), int>();
        var triangles = new Triangle[work.TriangleCount];
        var corners = new int[3];
        for (int triangle = 0; triangle < work.TriangleCount; triangle++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                int at = 3 * triangle + corner;
                Vector2 uv = work.Uvs?[at] ?? default;
                (int, Vector3, Vector2) key = (work.Corners[at], normals[at], uv);
                if (!indices.TryGetValue(key, out int vertex))
                {
                    vertex = indices[key] = vertices.Count;
                    vertices.Add(work.Positions[work.Corners[at]]);
                    vertexNormals.Add(normals[at]);
                    uvs?.Add(uv);
                }
                corners[corner] = vertex;
            }
            triangles[triangle] = new Triangle(corners[0], corners[1], corners[2]);
        }
        return new MeshEntity([.. vertices], triangles, [.. vertexNormals], uvs?.ToArray());
    }

    /// <summary>
    /// How far apart two meshes are: the largest and the mean distance from the vertices and the middles of the
    /// triangles of each to the surface of the other.
    /// </summary>
    public static (double Max, double Mean) Deviation(
        MeshEntity first, MeshEntity second, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        double max = 0, sum = 0;
        int count = 0;
        foreach ((MeshEntity from, MeshEntity to) in (ReadOnlySpan<(MeshEntity, MeshEntity)>)[(first, second), (second, first)])
        {
            var surface = new TriangleIndex(to);
            foreach (Point3d point in Samples(from))
            {
                if (++count % 2048 == 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                }
                double distance = surface.Distance(point);
                max = Math.Max(max, distance);
                sum += distance;
            }
        }
        return (max, count == 0 ? 0 : sum / count);
    }

    // The vertices and the middles of the triangles, at most some thousands of them.
    private static IEnumerable<Point3d> Samples(MeshEntity mesh)
    {
        int step = Math.Max(1, (mesh.Vertices.Length + mesh.Triangles.Length) / 20000);
        for (int i = 0; i < mesh.Vertices.Length; i += step)
        {
            yield return mesh.Vertices[i];
        }
        for (int i = 0; i < mesh.Triangles.Length; i += step)
        {
            Triangle t = mesh.Triangles[i];
            Point3d a = mesh.Vertices[t.A], b = mesh.Vertices[t.B], c = mesh.Vertices[t.C];
            yield return Point3d.Origin + ((a - Point3d.Origin) + (b - Point3d.Origin) + (c - Point3d.Origin)) / 3;
        }
    }
}
