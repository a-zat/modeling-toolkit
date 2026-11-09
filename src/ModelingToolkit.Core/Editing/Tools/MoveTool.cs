using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Moves an entity of the block being edited, the one selected or else the one clicked: a click takes the point to move
/// from, a second click the point to move to, both snapped to what is inferred, or a distance typed towards the
/// pointer. The arrow keys lock the direction on an axis; with Option (Alt) held, a copy is moved and the entity stays.
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
        Last = Infer(input.Pick, _start, Locked);
        Show();
    }

    public override void Click(ToolInput input)
    {
        Follow(input);
        Inference point = Infer(input.Pick, _start, Locked);
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
                Hint = NextHint;
                Show();
            }
            return;
        }
        MoveTo(start, point.Point);
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
            Host.ShowPreview(new ToolPreview(
                [new PreviewLine(start, last.Point, along), .. Transformed(Box, Transform.Translation(offset.X, offset.Y, offset.Z))],
                last));
            return;
        }
        Measurement = null;
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
