using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>What dragging a handle of the selection does.</summary>
public enum HandleKind
{
    /// <summary>A corner: moves the selection anywhere, snapping as the move tool does.</summary>
    Move,

    /// <summary>The middle of an edge: moves the selection along one of the axes only.</summary>
    Slide,

    /// <summary>The middle of a face: turns the selection around the normal of the face, snapping to right angles.</summary>
    Rotate,
}

/// <summary>A handle of the box of the selection, a point in the model space to drag.</summary>
/// <param name="Grip">The point to take the selection by, as the move tool snaps to it.</param>
public readonly record struct SelectionHandle(HandleKind Kind, Point3d Point, Inference Grip);

/// <summary>
/// The box of an entity of the block being edited, turned with it, in the model space, and the points to take it by:
/// its corners, which move it freely, the middles of its edges, which move it along the axes, and the middles of its
/// faces, which turn it around their normal.
/// </summary>
public sealed class SelectionHandles
{
    private static readonly int[][] Edges =
        [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];

    private static readonly int[][] Faces =
        [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 4, 5], [2, 3, 6, 7], [0, 2, 4, 6], [1, 3, 5, 7]];

    private readonly Point3d[] _corners; // of the box, the bit 1 for X, 2 for Y, 4 for Z

    /// <param name="placement">The placement in the model of the block that has the entity.</param>
    public SelectionHandles(Entity entity, Transform placement)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(placement);
        Entity = entity;
        Placement = placement;
        BoundingBox box;
        Transform frame = placement;
        if (entity is BlockReference reference)
        {
            box = reference.Block.GetBoundingBox();
            frame = placement * reference.Transform;
        }
        else
        {
            box = entity.GetBoundingBox();
        }
        IsEmpty = box.IsEmpty;
        _corners = IsEmpty ? [] : [.. Enumerable.Range(0, 8).Select(i => frame.Apply(new Point3d(
            (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z)))];
        Handles = IsEmpty ? [] : [.. Make()];
    }

    public Entity Entity { get; }

    /// <summary>The placement in the model of the block that has the entity.</summary>
    public Transform Placement { get; }

    public bool IsEmpty { get; }

    /// <summary>The corners, then the middles of the edges, then the middles of the faces.</summary>
    public IReadOnlyList<SelectionHandle> Handles { get; }

    /// <summary>The points to take the selection by, as the move and rotate tools snap to them.</summary>
    public IEnumerable<Inference> Grips => Handles.Select(handle => handle.Grip);

    /// <summary>The corners of the box, in the model space: bit 1 of the index for X, 2 for Y, 4 for Z.</summary>
    public IReadOnlyList<Point3d> Corners => _corners;

    /// <summary>The twelve edges of the box, transformed in the model space.</summary>
    public IEnumerable<PreviewLine> Box(Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        foreach (int[] edge in Edges)
        {
            yield return new PreviewLine(transform.Apply(_corners[edge[0]]), transform.Apply(_corners[edge[1]]));
        }
    }

    private IEnumerable<SelectionHandle> Make()
    {
        foreach (Point3d corner in _corners)
        {
            yield return new SelectionHandle(HandleKind.Move, corner, new Inference(InferenceKind.Endpoint, corner));
        }
        foreach (int[] edge in Edges)
        {
            Point3d a = _corners[edge[0]], b = _corners[edge[1]];
            if (a.DistanceTo(b) < 1e-9)
            {
                continue; // a flat box: the edge is a corner
            }
            Point3d middle = Middle(edge);
            yield return new SelectionHandle(HandleKind.Slide, middle,
                new Inference(InferenceKind.Midpoint, middle, Edge: (a, b)));
        }
        Point3d centre = Middle(0, 1, 2, 3, 4, 5, 6, 7);
        foreach (int[] face in Faces)
        {
            Point3d middle = Middle(face);
            Vector3d normal = middle - centre;
            if (normal.Length < 1e-9)
            {
                continue; // a flat box has no thickness to move across
            }
            normal = normal.Normalized();
            yield return new SelectionHandle(HandleKind.Rotate, middle, new Inference(InferenceKind.Centre, middle,
                new Plane(normal, normal.Dot(middle - Point3d.Origin)), normal));
        }
    }

    /// <summary>
    /// The turn of the selection by a handle of the middle of a face, dragged to the ray of the pointer: around the
    /// normal of the face, from the direction the pointer first went off its middle, snapping to right angles; null
    /// when the ray does not meet the plane of the face, or is still at its middle.
    /// </summary>
    /// <param name="from">The direction the turn starts from, set by the first drag far enough from the middle.</param>
    public static HandleDrag? Turn(SelectionHandle handle, PointerRay ray, ref Vector3d? from)
    {
        ArgumentNullException.ThrowIfNull(ray);
        if (handle.Grip is not { Plane: { } plane, Direction: { } axis } ||
            Math.Abs(ray.Direction.Dot(axis)) < 0.05 || InferenceEngine.Intersect(ray, plane) is not { } hit)
        {
            return null;
        }
        Vector3d to = hit - handle.Point;
        if (to.Length < 8 * ray.UnitSizeAt(handle.Point))
        {
            return from is null ? null : new HandleDrag(Transform.Identity, InferenceKind.None, [], 0, true);
        }
        from ??= to.Normalized();
        double degrees = RotateTool.Snap(Math.Atan2(from.Value.Cross(to).Dot(axis), from.Value.Dot(to)) * 180 / Math.PI);
        Transform rotation = Transform.Rotation(handle.Point, axis, degrees * Math.PI / 180);
        double radius = to.Length;
        return new HandleDrag(rotation, InferenceKind.None,
            [new PreviewLine(handle.Point, handle.Point + from.Value * radius),
             new PreviewLine(handle.Point, handle.Point + rotation.ApplyVector(from.Value) * radius)],
            degrees, true);
    }

    private Point3d Middle(params int[] corners) =>
        Point3d.Origin + corners.Aggregate(default(Vector3d), (sum, i) => sum + (_corners[i] - Point3d.Origin)) / corners.Length;
}

/// <summary>What a drag of a handle makes.</summary>
/// <param name="Transform">The transform of the selection, in the model space.</param>
/// <param name="Along">The axis it snapped along, or none.</param>
/// <param name="Lines">The lines that show the drag.</param>
/// <param name="Measure">The distance in millimetres, or the angle in degrees when <paramref name="IsAngle"/>.</param>
public sealed record HandleDrag(
    Transform Transform, InferenceKind Along, IReadOnlyList<PreviewLine> Lines, double Measure, bool IsAngle);
