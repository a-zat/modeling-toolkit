using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Draws lines: a click starts a line, each next click ends a segment, which is added to the geometry of the block
/// being edited, and starts the next one from there, until a segment closes a face or Escape. The points snap to what
/// the <see cref="InferenceEngine"/> infers; the arrow keys lock the direction on an axis, and a length typed in the
/// measurement field ends the segment at that length.
/// </summary>
public sealed class LineTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click to start a line.";
    private const string NextHint = "Click to end the segment or type its length · arrows lock an axis · Esc stops";

    private Point3d? _start;

    public override string Name => "Line";

    /// <summary>The point the segment being drawn starts from; null when no line is being drawn.</summary>
    public Point3d? Start => _start;

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick, _start, Locked);
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Inference point = Infer(input.Pick, _start, Locked);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_start is not { } start)
        {
            _start = point.Point;
            Hint = NextHint;
            Show();
            return;
        }
        DrawTo(start, point.Point);
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
        DrawTo(start, start + direction * length);
        return true;
    }

    private void DrawTo(Point3d start, Point3d end)
    {
        if (start.DistanceTo(end) < 1e-6)
        {
            return;
        }
        (bool drawn, bool madeFace) = Draw("Line", [new LineCurve(start, end)]);
        if (!drawn)
        {
            return;
        }
        if (madeFace)
        {
            Restart();
        }
        else
        {
            _start = end;
            Unlock();
            Show();
        }
    }

    protected override void Restart()
    {
        _start = null;
        Unlock();
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
            InferenceKind along = last.Direction is null ? InferenceKind.None : last.Kind;
            Host.ShowPreview(new ToolPreview([new PreviewLine(start, last.Point, along)], last));
            return;
        }
        Measurement = _start is null ? null : Measurements.Format(0);
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
