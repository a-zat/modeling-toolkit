using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Moves an entity of the block being edited, the one selected or else the one clicked: a click takes the point to move
/// from, a second click the point to move to, both snapped to what is inferred, or a distance typed towards the
/// pointer. The arrow keys lock the direction on an axis, and a point taken at the middle of a face of the box of the
/// selection locks it across that face; with Option (Alt) held, a copy is moved and the entity stays.
/// </summary>
public sealed class MoveTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string StartHint = "Click a point of the selection, or of what to move";
    private const string NextHint = "Click where to move or type the distance · arrows lock · ⌥ copies";

    private Point3d? _start;

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
                _start = point.Point;
                // Taken by the middle of a face of its box, it moves across that face only.
                if (point is { Kind: InferenceKind.Centre, Direction: { } across })
                {
                    LockTo(across);
                }
                Hint = NextHint;
                Show();
            }
            return;
        }
        MoveTo(start, point.Point);
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
