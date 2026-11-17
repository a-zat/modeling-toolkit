using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// A tool that draws curves into the geometry of the block being edited: the points it takes are inferred under the
/// pointer, snapping to the vertices near it, and what it draws is added with <see cref="IGeometryKernel.AddEdges"/>,
/// as one command. Points and curves are in the model space; they go into the block in its coordinates.
/// </summary>
public abstract class DrawingTool(IGeometryKernel kernel) : Tool
{
    private readonly Dictionary<IKernelShape, Point3d[]> _vertices = []; // of each shape, in its coordinates

    protected IGeometryKernel Kernel { get; } = kernel ?? throw new ArgumentNullException(nameof(kernel));

    protected InferenceEngine Inference { get; } = new();

    /// <summary>The point under the pointer, as last inferred; null before the pointer moves.</summary>
    protected Inference? Last { get; set; }

    /// <summary>The direction locked by an arrow key, of unit length; null for none.</summary>
    protected Vector3d? Locked { get; private set; }

    private InferenceKind _lockedKind = InferenceKind.None;

    /// <summary>
    /// Locks the direction on the axis of an arrow key (right X, left Y, up Z) or, for the down arrow, parallel to the
    /// edge last pointed at; the same key again unlocks it.
    /// </summary>
    protected void Lock(ToolKey key)
    {
        Transform placement = Host.Context.Placement;
        (InferenceKind kind, Vector3d? direction) = key switch
        {
            ToolKey.Right => (InferenceKind.AxisX, placement.ApplyVector(Vector3d.UnitX)),
            ToolKey.Left => (InferenceKind.AxisY, placement.ApplyVector(Vector3d.UnitY)),
            ToolKey.Up => (InferenceKind.AxisZ, placement.ApplyVector(Vector3d.UnitZ)),
            _ => (InferenceKind.Parallel, Inference.Reference),
        };
        bool unlock = _lockedKind == kind || direction is null;
        Locked = unlock ? null : direction!.Value.Normalized();
        _lockedKind = unlock ? InferenceKind.None : kind;
    }

    /// <summary>Locks the direction on one of unit length, as across the face a point was taken on.</summary>
    protected void LockTo(Vector3d direction)
    {
        Locked = direction.Normalized();
        _lockedKind = InferenceKind.Centre;
    }

    protected void Unlock()
    {
        Locked = null;
        _lockedKind = InferenceKind.None;
    }

    /// <summary>Drops what is being drawn, and shows the hint to start again.</summary>
    protected abstract void Restart();

    /// <summary>Shows what is being drawn, from <see cref="Last"/>.</summary>
    protected abstract void Show();

    protected override void OnActivated()
    {
        Inference.Reset();
        Restart();
    }

    protected override void OnDeactivated()
    {
        Host.ShowPreview(ToolPreview.Empty);
        Last = null;
        Measurement = null;
    }

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick);
        Show();
    }

    /// <summary>The point inferred under the pointer.</summary>
    protected Inference Infer(ToolPick pick, Point3d? from = null, Vector3d? locked = null)
    {
        Transform placement = Host.Context.Placement;
        Inference.Guides = [.. Host.Context.Block.Entities.OfType<GuideEntity>()
            .Select(guide => new GuideLine(placement.Apply(guide.Point), placement.ApplyVector(guide.Direction).Normalized()))];
        Inference found = Inference.Infer(pick, placement, from, locked, SnapVertices(pick), [.. Grips()]);
        if (locked is null && found.Kind is not (InferenceKind.Endpoint or InferenceKind.Midpoint or
                InferenceKind.Intersection or InferenceKind.Origin or InferenceKind.Centre) &&
            OnMeshEdge(pick) is { } onEdge)
        {
            return onEdge;
        }
        return found;
    }

    private readonly Dictionary<MeshEntity, Polylines> _meshEdges = [];

    // A point of the mesh under the pointer that the pointer passes near: an end of its edges, the middle of one, or a
    // point along one; the edges are those drawn, its sharp ones.
    private Inference? OnMeshEdge(ToolPick pick)
    {
        if (pick is not { Path: [.., MeshEntity mesh], Element: null, Ray: { } ray } || pick.Normal.Length == 0)
        {
            return null;
        }
        if (!_meshEdges.TryGetValue(mesh, out Polylines? edges))
        {
            if (_meshEdges.Count > 64)
            {
                _meshEdges.Clear();
            }
            edges = _meshEdges[mesh] = SharpEdges.Of(mesh.Vertices, mesh.Triangles, mesh.Normals);
        }
        Transform placement = InferenceEngine.PlacementOf(pick.Path);
        double unit = ray.UnitSizeAt(pick.Point);
        double snap = Inference.SnapDistance;
        double reach = 3 * snap * unit; // the edges near the point hit
        Inference? best = null;
        double bestUnits = snap;
        int rank = int.MaxValue; // ends before middles before points along
        for (int index = 0; index < edges.Count; index++)
        {
            ReadOnlySpan<Point3d> line = edges[index];
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Point3d a = placement.Apply(line[i]), b = placement.Apply(line[i + 1]);
                Point3d onSegment = Closest(pick.Point, a, b);
                if (onSegment.DistanceTo(pick.Point) > reach)
                {
                    continue;
                }
                Point3d middle = Point3d.Origin + ((a - Point3d.Origin) + (b - Point3d.Origin)) / 2;
                foreach ((Point3d point, InferenceKind kind, int order) in
                         (ReadOnlySpan<(Point3d, InferenceKind, int)>)
                         [(a, InferenceKind.Endpoint, 0), (b, InferenceKind.Endpoint, 0), (middle, InferenceKind.Midpoint, 1)])
                {
                    double units = RayDistance(ray, point) / ray.UnitSizeAt(point);
                    if (units < snap && (order < rank || order == rank && units < bestUnits))
                    {
                        best = new Inference(kind, point);
                        bestUnits = units;
                        rank = order;
                    }
                }
                if (rank <= 1)
                {
                    continue;
                }
                Point3d along = ClosestToRay(ray, a, b);
                double alongUnits = RayDistance(ray, along) / ray.UnitSizeAt(along);
                if (alongUnits < bestUnits)
                {
                    best = new Inference(InferenceKind.OnEdge, along, null, (b - a).Normalized());
                    bestUnits = alongUnits;
                    rank = 2;
                }
            }
        }
        return best;
    }

    private static Point3d Closest(Point3d point, Point3d a, Point3d b)
    {
        Vector3d ab = b - a;
        double length = ab.Dot(ab);
        return length == 0 ? a : a + ab * Math.Clamp((point - a).Dot(ab) / length, 0, 1);
    }

    private static double RayDistance(PointerRay ray, Point3d point) =>
        point.DistanceTo(ray.At(Math.Max((point - ray.Origin).Dot(ray.Direction), 0)));

    // The point of a segment closest to a ray.
    private static Point3d ClosestToRay(PointerRay ray, Point3d a, Point3d b)
    {
        Vector3d d = b - a;
        double length = d.Length;
        if (length == 0)
        {
            return a;
        }
        Vector3d u = d / length;
        Vector3d w = a - ray.Origin;
        double k = u.Dot(ray.Direction);
        double denominator = 1 - k * k;
        double s = denominator < 1e-12 ? 0 : (k * w.Dot(ray.Direction) - w.Dot(u)) / denominator;
        return a + u * Math.Clamp(s, 0, length);
    }

    /// <summary>Points to snap to besides the vertices of the geometry, as inferred, in the model space.</summary>
    protected virtual IEnumerable<Inference> Grips() => [];

    /// <summary>The plane to draw on from a point: that of the face it is on, otherwise the ground through it.</summary>
    protected Plane PlaneAt(Inference point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (point.Plane is { } plane)
        {
            return plane;
        }
        Vector3d up = Host.Context.Placement.ApplyVector(Vector3d.UnitZ).Normalized();
        return new Plane(up, up.Dot(point.Point - Point3d.Origin));
    }

    /// <summary>
    /// Two directions of unit length along a plane, at right angles: the X axis of the edit context laid on it (or
    /// its Y axis, for a plane across X), and the one after it, counterclockwise around the normal.
    /// </summary>
    protected (Vector3d U, Vector3d V) AxesOf(Plane plane)
    {
        Transform placement = Host.Context.Placement;
        Vector3d u = default;
        foreach (Vector3d axis in new[] { Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ })
        {
            Vector3d placed = placement.ApplyVector(axis).Normalized();
            u = placed - plane.Normal * placed.Dot(plane.Normal);
            if (u.Length > 1e-6)
            {
                break;
            }
        }
        u = u.Normalized();
        return (u, plane.Normal.Cross(u).Normalized());
    }

    /// <summary>The point of a plane under the pointer: the one inferred, brought onto the plane, or where the ray meets it.</summary>
    protected static Point3d OnPlane(Plane plane, Inference inferred, ToolPick? pick = null)
    {
        ArgumentNullException.ThrowIfNull(inferred);
        if (!inferred.IsSnapped && pick?.Ray is { } ray && InferenceEngine.Intersect(ray, plane) is { } hit)
        {
            return hit;
        }
        Point3d point = inferred.Point;
        return point - plane.Normal * (plane.Normal.Dot(point - Point3d.Origin) - plane.Offset);
    }

    /// <summary>
    /// Adds curves, in the model space, to the geometry of the block being edited, as one command; reports why when it
    /// cannot. Whether they were added, and whether they made a face.
    /// </summary>
    protected (bool Drawn, bool MadeFace) Draw(string name, IReadOnlyList<Curve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        Transform toBlock = Host.Context.Placement.Inverse();
        Curve[] local = [.. curves.Select(curve => Placed(curve, toBlock))];
        Block block = Host.Context.Block;
        BrepEntity? geometry = block.Entities.OfType<BrepEntity>().FirstOrDefault();
        try
        {
            ShapeEdit edit = Kernel.AddEdges(geometry?.Shape, local);
            IEditCommand command = geometry is null
                ? new AddEntityCommand(name, block, new BrepEntity(Kernel, edit.Shape))
                : new ReplaceShapeCommand(name, geometry, edit);
            Host.Session.Execute(command);
            return (true, edit.FaceOrigins.Contains(-1));
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The {name.ToLowerInvariant()} could not be drawn: {exception.Message}");
            return (false, false);
        }
    }

    private static Curve Placed(Curve curve, Transform transform) => curve switch
    {
        LineCurve line => new LineCurve(transform.Apply(line.Start), transform.Apply(line.End)),
        ArcCurve arc => new ArcCurve(transform.Apply(arc.Start), transform.Apply(arc.Through), transform.Apply(arc.End)),
        CircleCurve circle => new CircleCurve(transform.Apply(circle.Centre), transform.ApplyVector(circle.Normal),
            circle.Radius * transform.ApplyVector(Perpendicular(circle.Normal)).Length),
        _ => throw new ArgumentException($"A curve of type {curve.GetType().Name} cannot be drawn.", nameof(curve)),
    };

    /// <summary>
    /// A flat face of the geometry being edited, in the model space: its plane, with the normal the kernel gives it, its
    /// outline, the circles and arcs as segments, and its corners, the ends of its edges.
    /// </summary>
    protected sealed record FlatFace(
        BrepEntity Entity, int Index, Plane Plane, IReadOnlyList<PreviewLine> Outline, IReadOnlyList<Point3d> Corners);

    /// <summary>The flat face of the geometry being edited under the pointer; null for none.</summary>
    protected FlatFace? FlatFaceUnder(ToolPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (pick.Element is not { Kind: ElementKind.Face } element ||
            !Host.Context.Block.Entities.Contains(element.Entity) ||
            Kernel.GetFaceInfo(element.Entity.Shape, element.Index).Plane is not { } plane)
        {
            return null;
        }
        Transform placement = InferenceEngine.PlacementOf(element.Path);
        Vector3d normal = placement.ApplyVector(plane.Normal).Normalized();
        Point3d onPlane = placement.Apply(Point3d.Origin + plane.Normal * plane.Offset);
        EdgeInfo[] edges = [.. Kernel.GetFaceEdges(element.Entity.Shape, element.Index)
            .Select(edge => Kernel.GetEdgeInfo(element.Entity.Shape, edge))];
        return new FlatFace(element.Entity, element.Index, new Plane(normal, normal.Dot(onPlane - Point3d.Origin)),
            [.. edges.SelectMany(edge => LinesOf(edge, placement))],
            [.. edges.Where(edge => !edge.IsClosed).Select(edge => placement.Apply(edge.Start)).Distinct()]);
    }

    private static IEnumerable<PreviewLine> LinesOf(EdgeInfo info, Transform placement)
    {
        if (info.Circle is not { } circle)
        {
            return [new PreviewLine(placement.Apply(info.Start), placement.Apply(info.End))];
        }
        Vector3d start = info.Start - circle.Centre;
        Vector3d end = info.End - circle.Centre;
        double sweep = 2 * Math.PI;
        if (!info.IsClosed)
        {
            sweep = Math.Atan2(start.Cross(end).Dot(circle.Normal.Normalized()), start.Dot(end));
            sweep = sweep <= 0 ? sweep + 2 * Math.PI : sweep;
        }
        return Around(circle.Centre, start, circle.Normal, sweep)
            .Select(line => new PreviewLine(placement.Apply(line.Start), placement.Apply(line.End)));
    }

    /// <summary>A direction of unit length at right angles to another.</summary>
    protected static Vector3d Perpendicular(Vector3d direction)
    {
        Vector3d other = Math.Abs(direction.Normalized().X) < 0.9 ? Vector3d.UnitX : Vector3d.UnitY;
        return direction.Cross(other).Normalized();
    }

    /// <summary>Segments along a circle, from an angle, turning counterclockwise around the normal by a sweep.</summary>
    protected static IEnumerable<PreviewLine> Around(
        Point3d centre, Vector3d start, Vector3d normal, double sweep, int segments = 48)
    {
        Vector3d side = normal.Normalized().Cross(start);
        Point3d previous = centre + start;
        for (int i = 1; i <= segments; i++)
        {
            double angle = sweep * i / segments;
            Point3d next = centre + start * Math.Cos(angle) + side * Math.Sin(angle);
            yield return new PreviewLine(previous, next);
            previous = next;
        }
    }

    // The vertices the pointer snaps to near them: those of the geometry being edited, and of the shape it is on.
    private List<Point3d> SnapVertices(ToolPick pick)
    {
        var points = new List<Point3d>();
        Transform placement = Host.Context.Placement;
        List<BrepEntity> edited = [.. Host.Context.Block.Entities.OfType<BrepEntity>()];
        foreach (BrepEntity entity in edited)
        {
            points.AddRange(VerticesOf(entity).Select(placement.Apply));
        }
        if (pick.Element is { } element && !edited.Contains(element.Entity))
        {
            Transform shown = InferenceEngine.PlacementOf(element.Path);
            points.AddRange(VerticesOf(element.Entity).Select(shown.Apply));
        }
        return points;
    }

    private Point3d[] VerticesOf(BrepEntity entity)
    {
        if (!_vertices.TryGetValue(entity.Shape, out Point3d[]? vertices))
        {
            if (_vertices.Count > 64)
            {
                _vertices.Clear(); // of shapes replaced since
            }
            int count = entity.Kernel.CountElements(entity.Shape).Vertices;
            vertices = [.. Enumerable.Range(0, count).Select(vertex => entity.Kernel.GetVertexPoint(entity.Shape, vertex))];
            _vertices[entity.Shape] = vertices;
        }
        return vertices;
    }
}
