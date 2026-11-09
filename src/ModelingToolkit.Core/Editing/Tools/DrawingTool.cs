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
    protected Inference Infer(ToolPick pick, Point3d? from = null, Vector3d? locked = null) =>
        Inference.Infer(pick, Host.Context.Placement, from, locked, SnapVertices(pick));

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
