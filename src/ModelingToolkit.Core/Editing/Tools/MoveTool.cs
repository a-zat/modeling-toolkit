using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Moves an entity of the block being edited, the one selected or else the one clicked: a click takes the point to move
/// from, a second click the point to move to, both snapped to what is inferred, or a distance typed towards the
/// pointer. The arrow keys lock the direction on an axis; with Option (Alt) held, a copy is moved and the entity stays.
/// </summary>
public sealed class MoveTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click a point of the selection, or of what to move";
    private const string NextHint = "Click where to move or type the distance · arrows lock · ⌥ copies";

    private Entity? _target;
    private Point3d? _start;
    private bool _copy;
    private IReadOnlyList<PreviewLine> _box = [];

    public override string Name => "Move";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _copy = input.Modifiers.HasFlag(ToolModifiers.Alt);
        Last = Infer(input.Pick, _start, Locked);
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _copy = input.Modifiers.HasFlag(ToolModifiers.Alt);
        Inference point = Infer(input.Pick, _start, Locked);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_start is not { } start)
        {
            if (TargetOf(input.Pick) is not { } target)
            {
                Host.Report("Select an object, or click on one, to move it.");
                return;
            }
            _target = target;
            _start = point.Point;
            _box = BoxOf(target);
            Hint = NextHint;
            Show();
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

    // The entity of the block being edited that holds the selection, or else what is under the pointer.
    private Entity? TargetOf(ToolPick pick)
    {
        IReadOnlyList<Entity>? path = Host.SelectedEntity ?? pick.Path;
        return path is null ? null : Host.Context.Within(path) is { Count: > 0 } within ? within[0] : null;
    }

    // The edges of the box of an entity, in the model space.
    private List<PreviewLine> BoxOf(Entity entity)
    {
        BoundingBox box = entity.GetBoundingBox();
        if (box.IsEmpty)
        {
            return [];
        }
        Transform placement = Host.Context.Placement;
        Point3d Corner(int i) => placement.Apply(new Point3d(
            (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z));
        var lines = new List<PreviewLine>();
        for (int i = 0; i < 8; i++)
        {
            foreach (int bit in new[] { 1, 2, 4 })
            {
                if ((i & bit) == 0)
                {
                    lines.Add(new PreviewLine(Corner(i), Corner(i | bit)));
                }
            }
        }
        return lines;
    }

    private void MoveTo(Point3d start, Point3d end)
    {
        if (_target is not { } target || start.DistanceTo(end) < 1e-6)
        {
            return;
        }
        Vector3d along = Host.Context.Placement.Inverse().ApplyVector(end - start);
        try
        {
            Host.Session.Execute(TransformCommands.Of(
                _copy ? "Copy" : "Move", target, Transform.Translation(along.X, along.Y, along.Z), _copy));
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The object could not be moved: {exception.Message}");
            return;
        }
        Restart();
    }

    protected override void Restart()
    {
        _target = null;
        _start = null;
        _box = [];
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
                [new PreviewLine(start, last.Point, along), .. _box.Select(line => new PreviewLine(line.Start + offset, line.End + offset))],
                last));
            return;
        }
        Measurement = null;
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
