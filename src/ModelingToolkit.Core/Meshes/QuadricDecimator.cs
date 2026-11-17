using System.Numerics;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Meshes;

/// <summary>
/// Reduces the triangles of a mesh by collapsing its edges, the one that moves the surface least first, as measured by
/// the squared distances to the planes of the triangles around (Garland and Heckbert's quadrics). A collapse is not made
/// when it would fold a triangle over, join the surface to itself, or move a corner on a seam of the texture or, if
/// asked, on a border.
/// </summary>
internal sealed class QuadricDecimator
{
    private readonly WorkMesh _mesh;
    private readonly Quadric[] _quadrics;
    private readonly List<int>[] _triangles; // of each position, some may be dead
    private readonly bool[] _dead;
    private readonly bool[] _locked; // a seam of the texture or a border, kept
    private readonly bool[] _seam; // of the texture
    private readonly int[] _version;
    private readonly List<Point3d>[] _samples; // the points of the mesh as it was that each position stands for
    private double _maxError;
    private readonly TriangleIndex _original; // the surface as it was

    public QuadricDecimator(WorkMesh mesh, bool keepBorders, bool keepSeams = true)
    {
        _mesh = mesh;
        int positions = mesh.Positions.Count;
        _quadrics = new Quadric[positions];
        _triangles = new List<int>[positions];
        for (int i = 0; i < positions; i++)
        {
            _triangles[i] = [];
        }
        _dead = new bool[mesh.TriangleCount];
        _locked = new bool[positions];
        _seam = new bool[positions];
        _version = new int[positions];
        _original = new TriangleIndex(new Model.MeshEntity(
            [.. mesh.Positions],
            [.. Enumerable.Range(0, mesh.TriangleCount).Select(t =>
                new Geometry.Triangle(mesh.Corners[3 * t], mesh.Corners[3 * t + 1], mesh.Corners[3 * t + 2]))]));
        _samples = new List<Point3d>[positions];
        for (int i = 0; i < positions; i++)
        {
            _samples[i] = [mesh.Positions[i]];
        }

        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            Vector3d normal = mesh.AreaNormal(triangle);
            double length = normal.Length;
            for (int corner = 0; corner < 3; corner++)
            {
                _triangles[mesh.Corners[3 * triangle + corner]].Add(triangle);
            }
            if (length < 1e-12)
            {
                continue;
            }
            Vector3d unit = normal / length;
            var plane = Quadric.OfPlane(unit, -unit.Dot(mesh.Positions[mesh.Corners[3 * triangle]] - Point3d.Origin));
            for (int corner = 0; corner < 3; corner++)
            {
                _quadrics[mesh.Corners[3 * triangle + corner]] += plane;
            }
        }

        foreach (((int a, int b), List<int> around) in mesh.Edges())
        {
            if (around.Count == 1)
            {
                // A border: a plane across the triangle along it keeps the border from creeping in.
                int triangle = around[0];
                Vector3d normal = mesh.AreaNormal(triangle);
                Vector3d along = mesh.Positions[b] - mesh.Positions[a];
                Vector3d across = along.Cross(normal);
                if (across.Length > 1e-12)
                {
                    across = across.Normalized();
                    var plane = Quadric.OfPlane(across, -across.Dot(mesh.Positions[a] - Point3d.Origin)) * 100;
                    _quadrics[a] += plane;
                    _quadrics[b] += plane;
                }
                if (keepBorders)
                {
                    _locked[a] = _locked[b] = true;
                }
            }
            else if (around.Count > 2)
            {
                _locked[a] = _locked[b] = true; // where more than two triangles meet
            }
        }
        if (mesh.Uvs is { } uvs)
        {
            // The corners of a seam have more than one texture coordinate.
            var first = new Vector2?[positions];
            for (int corner = 0; corner < mesh.Corners.Count; corner++)
            {
                int position = mesh.Corners[corner];
                if (first[position] is not { } uv)
                {
                    first[position] = uvs[corner];
                }
                else if (Vector2.DistanceSquared(uv, uvs[corner]) > 1e-10f)
                {
                    _seam[position] = true;
                    _locked[position] |= keepSeams;
                }
            }
        }
    }

    /// <summary>
    /// Collapses edges until the triangles are no more than a target, or the next collapse would move the surface
    /// further than a distance (0 for no limit). Returns the largest distance moved, as estimated.
    /// </summary>
    public double Reduce(int target, double maxError, CancellationToken cancellation)
    {
        var queue = new PriorityQueue<(int From, int To, int FromVersion, int ToVersion, Point3d At), double>();
        void Push(int a, int b)
        {
            if (Cost(a, b) is { } cost)
            {
                queue.Enqueue((cost.From, cost.To, _version[cost.From], _version[cost.To], cost.At), cost.Error);
            }
        }
        foreach ((int a, int b) in _mesh.Edges().Keys)
        {
            Push(a, b);
        }

        int alive = _mesh.TriangleCount;
        double limit = maxError > 0 ? maxError * maxError : double.MaxValue;
        _maxError = maxError;
        double worst = 0;
        int steps = 0;
        while (alive > target && queue.TryDequeue(out var edge, out double error))
        {
            if (++steps % 4096 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }
            if (_version[edge.From] != edge.FromVersion || _version[edge.To] != edge.ToVersion)
            {
                continue; // changed since; pushed again then
            }
            if (error > limit)
            {
                break;
            }
            if (!CanCollapse(edge.From, edge.To, edge.At))
            {
                continue;
            }
            alive -= Collapse(edge.From, edge.To, edge.At);
            worst = Math.Max(worst, error);
            _version[edge.To]++;
            foreach (int neighbour in Neighbours(edge.To))
            {
                Push(edge.To, neighbour);
            }
        }

        _mesh.KeepTriangles(triangle => !_dead[triangle]);
        _mesh.Compact();
        return Math.Sqrt(worst);
    }

    // Which end goes into which, where they meet and the error of it; null when neither may move.
    private (int From, int To, Point3d At, double Error)? Cost(int a, int b)
    {
        if (_locked[a] && _locked[b])
        {
            return null;
        }
        Quadric sum = _quadrics[a] + _quadrics[b];
        Point3d pa = _mesh.Positions[a], pb = _mesh.Positions[b];
        if (_locked[a])
        {
            return (b, a, pa, Math.Max(sum.Error(pa), 0));
        }
        if (_locked[b])
        {
            return (a, b, pb, Math.Max(sum.Error(pb), 0));
        }
        Point3d middle = Point3d.Origin + ((pa - Point3d.Origin) + (pb - Point3d.Origin)) / 2;
        Point3d best = middle;
        double bestError = sum.Error(middle);
        // The optimum along the edge, which keeps the texture coordinates easy to follow.
        foreach (Point3d candidate in (ReadOnlySpan<Point3d>)[pa, pb, sum.Optimum(pa, pb)])
        {
            double candidateError = sum.Error(candidate);
            if (candidateError < bestError)
            {
                best = candidate;
                bestError = candidateError;
            }
        }
        return (a, b, best, Math.Max(bestError, 0));
    }

    private IEnumerable<int> Neighbours(int position)
    {
        var seen = new HashSet<int>();
        foreach (int triangle in _triangles[position])
        {
            if (_dead[triangle])
            {
                continue;
            }
            for (int corner = 0; corner < 3; corner++)
            {
                int other = _mesh.Corners[3 * triangle + corner];
                if (other != position && seen.Add(other))
                {
                    yield return other;
                }
            }
        }
    }

    private bool CanCollapse(int from, int to, Point3d at)
    {
        // The link condition: the positions next to both are those across the triangles of the edge, so that the
        // surface is not joined to itself.
        var fromNeighbours = new HashSet<int>(Neighbours(from));
        int shared = 0, across = 0;
        foreach (int neighbour in Neighbours(to))
        {
            if (fromNeighbours.Contains(neighbour))
            {
                shared++;
            }
        }
        foreach (int triangle in _triangles[from])
        {
            if (!_dead[triangle] && Has(triangle, to))
            {
                across++;
            }
        }
        if (across == 0 || shared != across)
        {
            return false;
        }

        // No triangle that stays folds over, or loses its area.
        var fan = new List<(Point3d, Point3d, Point3d)>();
        foreach (int position in (ReadOnlySpan<int>)[from, to])
        {
            foreach (int triangle in _triangles[position])
            {
                if (_dead[triangle] || Has(triangle, from) && Has(triangle, to))
                {
                    continue;
                }
                Vector3d before = _mesh.AreaNormal(triangle);
                Point3d[] corners = new Point3d[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = _mesh.Corners[3 * triangle + corner];
                    corners[corner] = index == from || index == to ? at : _mesh.Positions[index];
                }
                Vector3d after = (corners[1] - corners[0]).Cross(corners[2] - corners[0]);
                if (after.Length < 1e-9 * Math.Max(before.Length, 1e-12) ||
                    before.Dot(after) < 0.85 * before.Length * after.Length)
                {
                    return false;
                }
                fan.Add((corners[0], corners[1], corners[2]));
            }
        }

        // The points of the mesh as it was that both ends stand for stay near the triangles around the point, and those
        // triangles near the surface as it was.
        if (_maxError > 0 && fan.Count > 0)
        {
            if (_original.Distance(at) > _maxError)
            {
                return false;
            }
            foreach ((Point3d a, Point3d b, Point3d c) in fan)
            {
                Point3d middle = Point3d.Origin + ((a - Point3d.Origin) + (b - Point3d.Origin) + (c - Point3d.Origin)) / 3;
                foreach (Point3d point in (ReadOnlySpan<Point3d>)[middle, Mid(a, b), Mid(b, c), Mid(c, a)])
                {
                    if (_original.Distance(point) > _maxError)
                    {
                        return false;
                    }
                }
            }
            foreach (List<Point3d> samples in (ReadOnlySpan<List<Point3d>>)[_samples[from], _samples[to]])
            {
                foreach (Point3d sample in samples)
                {
                    double nearest = double.MaxValue;
                    foreach ((Point3d a, Point3d b, Point3d c) in fan)
                    {
                        nearest = Math.Min(nearest, TriangleIndex.ToTriangle(sample, a, b, c));
                        if (nearest <= _maxError)
                        {
                            break;
                        }
                    }
                    if (nearest > _maxError)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private static Point3d Mid(Point3d a, Point3d b) => Point3d.Origin + ((a - Point3d.Origin) + (b - Point3d.Origin)) / 2;

    private bool Has(int triangle, int position) =>
        _mesh.Corners[3 * triangle] == position || _mesh.Corners[3 * triangle + 1] == position ||
        _mesh.Corners[3 * triangle + 2] == position;

    // Moves the end that goes, and the one that stays, to the point; returns the triangles taken out.
    private int Collapse(int from, int to, Point3d at)
    {
        List<Vector2>? uvs = _mesh.Uvs;
        Point3d start = _mesh.Positions[to], end = _mesh.Positions[from];
        // How far along the edge the point is, from the end that stays, for the texture coordinates.
        Vector3d edge = end - start;
        double t = edge.Dot(edge) > 0 ? Math.Clamp((at - start).Dot(edge) / edge.Dot(edge), 0, 1) : 0;

        // The texture coordinates of both ends, as in a triangle of the edge.
        Vector2 uvTo = default, uvFrom = default;
        if (uvs is not null)
        {
            foreach (int triangle in _triangles[from])
            {
                if (!_dead[triangle] && Has(triangle, to))
                {
                    uvTo = uvs[CornerOf(triangle, to)];
                    uvFrom = uvs[CornerOf(triangle, from)];
                    break;
                }
            }
        }
        var uv = Vector2.Lerp(uvTo, uvFrom, (float)t);

        // The normal of the end that stays, as in a triangle of the edge, for the corners that came from the other.
        List<Vector3>? normals = _mesh.Normals;
        Vector3 normalTo = default;
        if (normals is not null)
        {
            foreach (int triangle in _triangles[from])
            {
                if (!_dead[triangle] && Has(triangle, to))
                {
                    normalTo = normals[CornerOf(triangle, to)];
                    break;
                }
            }
        }

        int removed = 0;
        foreach (int triangle in _triangles[from])
        {
            if (_dead[triangle])
            {
                continue;
            }
            if (Has(triangle, to))
            {
                _dead[triangle] = true;
                removed++;
                continue;
            }
            int corner = CornerOf(triangle, from);
            _mesh.Corners[corner] = to;
            if (normals is not null)
            {
                normals[corner] = normalTo;
            }
            _triangles[to].Add(triangle);
        }
        if (uvs is not null && !_seam[to] && !_seam[from])
        {
            // Both ends are inside one region of the texture: their corners take the coordinates of the point.
            foreach (int triangle in _triangles[to])
            {
                if (!_dead[triangle])
                {
                    uvs[CornerOf(triangle, to)] = uv;
                }
            }
        }
        else if (uvs is not null)
        {
            // The end that stays is on a seam: the corners that came from the other end take its coordinates there.
            foreach (int triangle in _triangles[from])
            {
                if (!_dead[triangle])
                {
                    uvs[CornerOf(triangle, to)] = uvTo;
                }
            }
        }
        _triangles[from].Clear();
        _mesh.Positions[to] = at;
        _quadrics[to] += _quadrics[from];
        _samples[to].AddRange(_samples[from]);
        _samples[from] = [];
        _version[from]++;
        return removed;
    }

    private int CornerOf(int triangle, int position)
    {
        for (int corner = 0; corner < 3; corner++)
        {
            if (_mesh.Corners[3 * triangle + corner] == position)
            {
                return 3 * triangle + corner;
            }
        }
        throw new InvalidOperationException("The triangle has no such corner.");
    }

    /// <summary>The sum of the squared distances to planes, as a symmetric 4 × 4 matrix.</summary>
    private readonly record struct Quadric(
        double Xx, double Xy, double Xz, double Xw, double Yy, double Yz, double Yw, double Zz, double Zw, double Ww)
    {
        public static Quadric OfPlane(Vector3d n, double d) =>
            new(n.X * n.X, n.X * n.Y, n.X * n.Z, n.X * d, n.Y * n.Y, n.Y * n.Z, n.Y * d, n.Z * n.Z, n.Z * d, d * d);

        public static Quadric operator +(Quadric a, Quadric b) => new(
            a.Xx + b.Xx, a.Xy + b.Xy, a.Xz + b.Xz, a.Xw + b.Xw, a.Yy + b.Yy, a.Yz + b.Yz, a.Yw + b.Yw, a.Zz + b.Zz,
            a.Zw + b.Zw, a.Ww + b.Ww);

        public static Quadric operator *(Quadric a, double s) => new(
            a.Xx * s, a.Xy * s, a.Xz * s, a.Xw * s, a.Yy * s, a.Yz * s, a.Yw * s, a.Zz * s, a.Zw * s, a.Ww * s);

        public double Error(Point3d p) =>
            Xx * p.X * p.X + 2 * Xy * p.X * p.Y + 2 * Xz * p.X * p.Z + 2 * Xw * p.X +
            Yy * p.Y * p.Y + 2 * Yz * p.Y * p.Z + 2 * Yw * p.Y + Zz * p.Z * p.Z + 2 * Zw * p.Z + Ww;

        // The point of least error on the segment between two points: the quadric is a parabola along it.
        public Point3d Optimum(Point3d a, Point3d b)
        {
            double e0 = Error(a), e1 = Error(b);
            Point3d middle = Point3d.Origin + ((a - Point3d.Origin) + (b - Point3d.Origin)) / 2;
            double em = Error(middle);
            // e(t) = e0 + (−3e0 + 4em − e1) t + (2e0 − 4em + 2e1) t²
            double c2 = 2 * e0 - 4 * em + 2 * e1, c1 = -3 * e0 + 4 * em - e1;
            double t = c2 > 1e-18 ? Math.Clamp(-c1 / (2 * c2), 0, 1) : 0.5;
            return a + (b - a) * t;
        }
    }
}
