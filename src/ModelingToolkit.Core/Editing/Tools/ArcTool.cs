using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Draws arcs by their ends and their bulge: a click sets the start, a second click the end (or a typed length of the
/// chord, towards the pointer), and a third how far the arc bulges from the chord (or a typed distance), on the face the
/// start is on, else on the ground, or else on the upright plane through the chord.
/// </summary>
public sealed class ArcTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click to start the arc.";
    private const string EndHint = "Click the end of the arc or type the length of the chord · Esc stops";
    private const string BulgeHint = "Click how far the arc bulges or type the distance · Esc stops";

    private Point3d? _start;
    private Point3d? _end;
    private Plane _plane;
    private double _bulge;

    public override string Name => "Arc";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick, _end is null ? _start : null);
        if (_end is not null)
        {
            _bulge = BulgeTo(OnPlane(_plane, Last, input.Pick));
        }
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Inference point = Infer(input.Pick, _end is null ? _start : null);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_start is not { } start)
        {
            _start = point.Point;
            _plane = PlaneAt(point);
            MeasurementName = "Length";
            Hint = EndHint;
        }
        else if (_end is null)
        {
            SetEnd(start, point.Point);
        }
        else
        {
            DrawWith(BulgeTo(OnPlane(_plane, point, input.Pick)));
            return;
        }
        Show();
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _start is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    public override bool TypeMeasurement(string text)
    {
        if (_start is not { } start || !Measurements.TryParseLength(text, out double length))
        {
            return false;
        }
        if (_end is null)
        {
            Vector3d direction = ((Last?.Point ?? start) - start).Normalized();
            if (direction.Length == 0)
            {
                return false;
            }
            SetEnd(start, start + direction * length);
            Show();
            return true;
        }
        DrawWith(_bulge < 0 ? -length : length);
        return true;
    }

    private void SetEnd(Point3d start, Point3d end)
    {
        if (start.DistanceTo(end) < 1e-6)
        {
            return;
        }
        // The plane of the start, if the chord lies on it, otherwise the upright plane through the chord.
        Vector3d chord = (end - start).Normalized();
        if (Math.Abs(_plane.Normal.Dot(chord)) > 1e-9)
        {
            Vector3d up = Host.Context.Placement.ApplyVector(Vector3d.UnitZ).Normalized();
            Vector3d normal = chord.Cross(up).Normalized();
            if (normal.Length == 0)
            {
                normal = Perpendicular(chord);
            }
            _plane = new Plane(normal, normal.Dot(start - Point3d.Origin));
        }
        _end = end;
        _bulge = 0;
        MeasurementName = "Bulge";
        Hint = BulgeHint;
    }

    // Across the chord, in the plane, towards the left of the chord seen from the normal.
    private Vector3d Across => _plane.Normal.Cross((_end!.Value - _start!.Value).Normalized()).Normalized();

    private Point3d Middle => _start!.Value + (_end!.Value - _start!.Value) * 0.5;

    private double BulgeTo(Point3d point) => (point - Middle).Dot(Across);

    private void DrawWith(double bulge)
    {
        if (Math.Abs(bulge) < 1e-6)
        {
            Host.Report("An arc needs to bulge from the line between its ends.");
            return;
        }
        if (Draw("Arc", [new ArcCurve(_start!.Value, Middle + Across * bulge, _end!.Value)]).Drawn)
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _start = null;
        _end = null;
        MeasurementName = "Length";
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
        if (_start is { } start && _end is { } end)
        {
            Measurement = Measurements.Format(Math.Abs(_bulge));
            List<PreviewLine> lines = [new PreviewLine(start, end)];
            double half = start.DistanceTo(end) / 2;
            if (Math.Abs(_bulge) > 1e-6)
            {
                // The circle through the ends and the top of the bulge, and the sweep from the start to the end.
                double radius = (half * half + _bulge * _bulge) / (2 * Math.Abs(_bulge));
                Point3d centre = Middle + Across * (_bulge - Math.Sign(_bulge) * radius);
                double sweep = 4 * Math.Atan(_bulge / half);
                lines.AddRange(Around(centre, start - centre, _plane.Normal, -sweep));
            }
            Host.ShowPreview(new ToolPreview(lines, Last));
            return;
        }
        if (_start is { } from && Last is { Kind: not InferenceKind.None } last)
        {
            Measurement = Measurements.Format(from.DistanceTo(last.Point));
            Host.ShowPreview(new ToolPreview([new PreviewLine(from, last.Point, last.Direction is null ? InferenceKind.None : last.Kind)], last));
            return;
        }
        Measurement = _start is null ? null : Measurements.Format(0);
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
