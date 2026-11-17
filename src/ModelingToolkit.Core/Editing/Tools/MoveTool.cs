using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Moves an entity of the block being edited, the one selected or else the one clicked: a click takes the point to move
/// from, a second click the point to move to, both snapped to what is inferred, or a distance typed towards the
/// pointer. The arrow keys lock the direction on an axis; a point taken at the middle of a face of the box of the
/// selection locks it across that face, and one at the middle of an edge on the axes; with Option (Alt) held, a copy is moved and the entity stays.
/// </summary>
public sealed class MoveTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string StartHint = "Click a point of the selection, or of what to move";
    private const string NextHint = "Click where to move or type the distance · arrows lock · ⌥ copies";

    private Point3d? _start;
    private bool _alongAxes; // taken by the middle of an edge of its box

    public override string Name => "Move";

    public override void Hover(ToolInput input)
    {
        Follow(input);
        Last = Track(input.Pick);
        Show();
    }

    public override void Click(ToolInput input)
    {
        Follow(input);
        Inference point = Track(input.Pick);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_start is not { } start)
        {
            if (TakeTarget(input.Pick))
            {
                Start(point);
            }
            return;
        }
        MoveTo(start, point.Point);
    }

    /// <summary>
    /// Takes the selection by one of its points to take it by, as a click there would, to move it from there; whether
    /// there is a selection to take.
    /// </summary>
    public bool Grab(Inference grip)
    {
        ArgumentNullException.ThrowIfNull(grip);
        Restart();
        if (!TakeTarget(ToolPick.Nothing))
        {
            return false;
        }
        Last = grip;
        Start(grip);
        return true;
    }

    // Starts moving from a point: taken by the middle of a face of the box of the selection, it moves across that face
    // only; by the middle of an edge, along the axes only; elsewhere, anywhere.
    private void Start(Inference point)
    {
        _alongAxes = IsEdgeGrip(point);
        _start = point.Point;
        if (point is { Kind: InferenceKind.Centre, Direction: { } across })
        {
            LockTo(across);
        }
        Hint = NextHint;
        Show();
    }

    // The point under the pointer: once moving, what is under the target is not taken, and off everything else the
    // target stays at the height it was taken at.
    private Inference Track(ToolPick pick)
    {
        if (_start is not { } start)
        {
            return Infer(pick);
        }
        ToolPick clear = Clear(pick);
        Inference point = Infer(clear, start, Locked);
        if (_alongAxes && Locked is null && point.Kind != InferenceKind.None)
        {
            return AlongAxis(start, point);
        }
        if (point.Kind is InferenceKind.Ground or InferenceKind.None && clear.Ray is { } ray)
        {
            Vector3d up = Host.Context.Placement.ApplyVector(Vector3d.UnitZ).Normalized();
            var level = new Plane(up, up.Dot(start - Point3d.Origin));
            if (InferenceEngine.Intersect(ray, level) is { } hit)
            {
                return new Inference(InferenceKind.Ground, hit, level);
            }
        }
        return point;
    }

    // The point brought onto the axis of the edit context nearest to the way it is from the start.
    private Inference AlongAxis(Point3d start, Inference point)
    {
        Transform placement = Host.Context.Placement;
        Vector3d offset = point.Point - start;
        (InferenceKind Kind, Vector3d Axis)[] axes =
        [
            (InferenceKind.AxisX, placement.ApplyVector(Vector3d.UnitX).Normalized()),
            (InferenceKind.AxisY, placement.ApplyVector(Vector3d.UnitY).Normalized()),
            (InferenceKind.AxisZ, placement.ApplyVector(Vector3d.UnitZ).Normalized()),
        ];
        (InferenceKind kind, Vector3d axis) = axes.MaxBy(entry => Math.Abs(entry.Axis.Dot(offset)));
        Point3d along = start + axis * axis.Dot(offset);
        // A point snapped to keeps its kind, and the edge it is on, where it is on the axis.
        return point.IsSnapped && along.DistanceTo(point.Point) < 1e-6
            ? point
            : new Inference(kind, along, null, axis);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        switch (key)
        {
            case ToolKey.Escape when _start is not null:
                Restart();
                return true;
            case ToolKey.Right or ToolKey.Left or ToolKey.Up or ToolKey.Down when _start is not null:
                Lock(key);
                return true;
            default:
                return false;
        }
    }

    public override bool TypeMeasurement(string text)
    {
        if (_start is not { } start || Last is not { } last || !Measurements.TryParseLength(text, out double length))
        {
            return false;
        }
        Vector3d direction = (last.Point - start).Normalized();
        if (direction.Length == 0)
        {
            return false;
        }
        MoveTo(start, start + direction * length);
        return true;
    }

    private void MoveTo(Point3d start, Point3d end)
    {
        Vector3d along = end - start;
        if (along.Length < 1e-6)
        {
            return;
        }
        if (Apply(Transform.Translation(along.X, along.Y, along.Z)))
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _start = null;
        _alongAxes = false;
        ReleaseTarget();
        Unlock();
        MeasurementName = "Distance";
        Hint = StartHint;
        Measurement = null;
        Show();
    }

    protected override void Show()
    {
        if (!IsActive)
        {
            return;
        }
        if (_start is { } start && Last is { Kind: not InferenceKind.None } last)
        {
            Measurement = Measurements.Format(start.DistanceTo(last.Point));
            Vector3d offset = last.Point - start;
            InferenceKind along = last.Direction is null ? InferenceKind.None : last.Kind;
            Transform moved = Transform.Translation(offset.X, offset.Y, offset.Z);
            ShowTransformed(Copy ? null : moved);
            Host.ShowPreview(new ToolPreview(
                [new PreviewLine(start, last.Point, along), .. Transformed(Box, moved)],
                last));
            return;
        }
        Measurement = null;
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
