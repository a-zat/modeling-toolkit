using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>What dragging a handle of the selection does.</summary>
public enum HandleKind
{
    /// <summary>Moves the selection across the ground, snapping along the axes.</summary>
    Move,

    /// <summary>Moves the selection up or down.</summary>
    Lift,

    /// <summary>Turns the selection around the vertical through the middle of its bottom, snapping to right angles.</summary>
    Rotate,
}

/// <summary>A handle of the box of the selection, a point in the model space to drag.</summary>
public readonly record struct SelectionHandle(HandleKind Kind, Point3d Point);

/// <summary>
/// The handles of the box of an entity of the block being edited, turned with it, and the transform a drag of one of
/// them makes, in the model space: the middle of the bottom moves it across the ground, the middle of the top lifts it,
/// and the middles of the edges of the bottom turn it.
/// </summary>
public sealed class SelectionHandles
{
    private readonly Point3d[] _corners; // of the box, the bit 1 for X, 2 for Y, 4 for Z
    private readonly Vector3d[] _axes; // of the block being edited, in the model space
    private readonly Point3d _bottom;

    /// <param name="placement">The placement in the model of the block that has the entity.</param>
    public SelectionHandles(Entity entity, Transform placement)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(placement);
        Entity = entity;
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
        _axes = [.. new[] { Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ }.Select(axis => placement.ApplyVector(axis).Normalized())];
        Placement = placement;
        if (IsEmpty)
        {
            Handles = [];
            return;
        }
        _bottom = Middle(0, 1, 2, 3);
        Handles =
        [
            new(HandleKind.Move, _bottom),
            new(HandleKind.Lift, Middle(4, 5, 6, 7)),
            new(HandleKind.Rotate, Middle(0, 1)),
            new(HandleKind.Rotate, Middle(1, 3)),
            new(HandleKind.Rotate, Middle(3, 2)),
            new(HandleKind.Rotate, Middle(2, 0)),
        ];
    }

    public Entity Entity { get; }

    /// <summary>The placement in the model of the block that has the entity.</summary>
    public Transform Placement { get; }

    public bool IsEmpty { get; }

    public IReadOnlyList<SelectionHandle> Handles { get; }

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

    private static readonly int[][] Edges =
        [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];

    /// <summary>The vertical of the block being edited, of unit length.</summary>
    public Vector3d Up => _axes[2];

    /// <summary>
    /// The drag of a handle to the ray of the pointer: the transform it makes, in the model space, what it snapped
    /// along, and the measure of it, a distance in millimetres or an angle in degrees; null when the ray does not
    /// meet the plane of the drag.
    /// </summary>
    public HandleDrag? Drag(SelectionHandle handle, PointerRay ray)
    {
        ArgumentNullException.ThrowIfNull(ray);
        var level = new Plane(Up, Up.Dot(handle.Point - Point3d.Origin));
        switch (handle.Kind)
        {
            case HandleKind.Lift:
            {
                double lift = AlongLine(ray, handle.Point, Up);
                Vector3d by = Up * lift;
                return new HandleDrag(Transform.Translation(by.X, by.Y, by.Z), InferenceKind.AxisZ,
                    [new PreviewLine(handle.Point, handle.Point + by, InferenceKind.AxisZ)], Math.Abs(lift), false);
            }
            case HandleKind.Move:
            {
                if (InferenceEngine.Intersect(ray, level) is not { } hit)
                {
                    return null;
                }
                Vector3d by = hit - handle.Point;
                InferenceKind along = InferenceKind.None;
                double limit = Math.Sin(TransformTool.AxisAngle * Math.PI / 180);
                for (int i = 0; i < 2 && by.Length > 1e-9; i++)
                {
                    double length = by.Dot(_axes[i]);
                    if ((by - _axes[i] * length).Length < limit * by.Length)
                    {
                        by = _axes[i] * length;
                        along = i == 0 ? InferenceKind.AxisX : InferenceKind.AxisY;
                    }
                }
                return new HandleDrag(Transform.Translation(by.X, by.Y, by.Z), along,
                    [new PreviewLine(handle.Point, handle.Point + by, along)], by.Length, false);
            }
            default:
            {
                if (InferenceEngine.Intersect(ray, level) is not { } hit)
                {
                    return null;
                }
                Point3d centre = _bottom + Up * Up.Dot(handle.Point - _bottom);
                Vector3d from = handle.Point - centre, to = hit - centre;
                to -= Up * to.Dot(Up);
                if (from.Length < 1e-9 || to.Length < 1e-9)
                {
                    return null;
                }
                double degrees = RotateTool.Snap(Math.Atan2(from.Cross(to).Dot(Up), from.Dot(to)) * 180 / Math.PI);
                Transform rotation = Transform.Rotation(_bottom, Up, degrees * Math.PI / 180);
                return new HandleDrag(rotation, InferenceKind.None,
                    [new PreviewLine(centre, handle.Point), new PreviewLine(centre, rotation.Apply(handle.Point))],
                    degrees, true);
            }
        }
    }

    private Point3d Middle(params int[] corners) =>
        Point3d.Origin + corners.Aggregate(default(Vector3d), (sum, i) => sum + (_corners[i] - Point3d.Origin)) / corners.Length;

    // How far along a line through a point, a direction of unit length, its point closest to the ray is.
    private static double AlongLine(PointerRay ray, Point3d point, Vector3d direction)
    {
        Vector3d between = point - ray.Origin;
        double b = direction.Dot(ray.Direction);
        double denominator = 1 - b * b;
        return denominator < 1e-12 ? 0 : (b * between.Dot(ray.Direction) - between.Dot(direction)) / denominator;
    }
}

/// <summary>What a drag of a handle makes.</summary>
/// <param name="Transform">The transform of the selection, in the model space.</param>
/// <param name="Along">The axis it snapped along, or none.</param>
/// <param name="Lines">The lines that show the drag.</param>
/// <param name="Measure">The distance in millimetres, or the angle in degrees when <paramref name="IsAngle"/>.</param>
public sealed record HandleDrag(
    Transform Transform, InferenceKind Along, IReadOnlyList<PreviewLine> Lines, double Measure, bool IsAngle);
