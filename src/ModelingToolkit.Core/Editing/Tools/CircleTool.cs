using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Draws circles: a click sets the centre, on the face it is on or else on the ground, and a second click the radius,
/// which can be typed instead. A circle makes a disc, and splits the face it is drawn on.
/// </summary>
public sealed class CircleTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click to place the centre.";
    private const string NextHint = "Click to set the radius or type it · Esc stops";

    private Point3d? _centre;
    private Plane _plane;
    private Point3d _rim;

    public override string Name => "Circle";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick, _centre);
        if (_centre is not null)
        {
            _rim = OnPlane(_plane, Last, input.Pick);
        }
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Inference point = Infer(input.Pick, _centre);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_centre is not { } centre)
        {
            _centre = point.Point;
            _plane = PlaneAt(point);
            _rim = point.Point;
            Hint = NextHint;
            Show();
            return;
        }
        _rim = OnPlane(_plane, point, input.Pick);
        DrawWith(centre, centre.DistanceTo(_rim));
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _centre is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    public override bool TypeMeasurement(string text)
    {
        if (_centre is not { } centre || !Measurements.TryParseLength(text, out double radius))
        {
            return false;
        }
        DrawWith(centre, radius);
        return true;
    }

    private void DrawWith(Point3d centre, double radius)
    {
        if (radius < 1e-6)
        {
            return;
        }
        if (Draw("Circle", [new CircleCurve(centre, _plane.Normal, radius)]).Drawn)
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _centre = null;
        MeasurementName = "Radius";
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
        if (_centre is { } centre)
        {
            double radius = centre.DistanceTo(_rim);
            Measurement = Measurements.Format(radius);
            List<PreviewLine> lines = [new PreviewLine(centre, _rim)];
            if (radius > 1e-6)
            {
                lines.AddRange(Around(centre, _rim - centre, _plane.Normal, 2 * Math.PI));
            }
            Host.ShowPreview(new ToolPreview(lines, Last));
            return;
        }
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
