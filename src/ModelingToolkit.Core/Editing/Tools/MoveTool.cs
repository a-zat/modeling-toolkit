using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Moves an entity of the block being edited, the one selected or else the one clicked: a click takes the point to move
/// from, a second click the point to move to, both snapped to what is inferred, or a distance typed towards the
/// pointer, or three lengths along X, Y and Z ("500, 0, 0"). The arrow keys, or X, Y and Z, lock the direction on an
/// axis; with Option (Alt) held, a copy is moved and the entity stays. Taken by a handle of the select tool, the middle
/// of a face of the box moves it across that face only, and the middle of an edge along the axes only.
/// </summary>
public sealed class MoveTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string StartHint = "Click a point of the selection, or of what to move";
    private const string NextHint = "Click where to move or type the distance · arrows lock · ⌥ copies";

    private Point3d? _start;
    private bool _alongAxes; // taken by the middle of an edge of its box

    public override Point3d? TakenAt => _start;

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
                Start(point, constrained: false);
            }
            return;
        }
        MoveTo(start, point.Point);
    }

    /// <summary>
    /// Takes the selection by one of its points to take it by, as a click there would, to move it from there; whether
    /// there is a selection to take.
    /// </summary>
    public override bool Grab(Inference grip) => Grab(grip, constrained: false);

    /// <summary>
    /// Takes the selection by a point of its box, as a handle of the select tool does: constrained, the middle of a face
    /// or of an edge moves it along the axes only.
    /// </summary>
    public bool Grab(Inference grip, bool constrained)
    {
        ArgumentNullException.ThrowIfNull(grip);
        Restart();
        if (!TakeTarget(ToolPick.Nothing))
        {
            return false;
        }
        Last = grip;
        Start(grip, constrained);
        return true;
    }

    // Starts moving from a point, anywhere; constrained, from the middle of a face or of an edge of the box of the
    // selection along the axes only.
    private void Start(Inference point, bool constrained)
    {
        _alongAxes = constrained && (IsEdgeGrip(point) || point.Kind == InferenceKind.Centre);
        _start = point.Point;
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

    // The point brought onto the axis nearest to the way it is from the start: of the component moved, those of its box
    // whose handle took it, else of the edit context.
    private Inference AlongAxis(Point3d start, Inference point)
    {
        Transform placement = Host.Context.Placement;
        Vector3d offset = point.Point - start;
        IReadOnlyList<Vector3d> frame = ObjectAxes ??
        [
            placement.ApplyVector(Vector3d.UnitX).Normalized(), placement.ApplyVector(Vector3d.UnitY).Normalized(),
            placement.ApplyVector(Vector3d.UnitZ).Normalized(),
        ];
        (InferenceKind Kind, Vector3d Axis)[] axes =
            [(InferenceKind.AxisX, frame[0]), (InferenceKind.AxisY, frame[1]), (InferenceKind.AxisZ, frame[2])];
        (InferenceKind kind, Vector3d axis) = axes.MaxBy(entry => Math.Abs(entry.Axis.Dot(offset)));
        Point3d along = start + axis * axis.Dot(offset);
        // A point snapped to keeps its kind, and the edge it is on, where it is on the axis.
        return point.IsSnapped && along.DistanceTo(point.Point) < 1e-6
            ? point
            : new Inference(kind, along, null, axis) { OfObject = !IsContextAxis(axis) };
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
        // Three lengths move by them along the axes of the block being edited.
        if (_start is { } from && Measurements.TryParseOffset(text, out double x, out double y, out double z))
        {
            Vector3d by = Host.Context.Placement.ApplyVector(new Vector3d(x, y, z));
            MoveTo(from, from + by);
            return true;
        }
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
