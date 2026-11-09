using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Draws rectangles: a click sets a corner, on the face it is on or else on the ground, and a second click the opposite
/// corner, with the sides along the axes of the edit context laid on that plane. Its size can be typed instead
/// ("1200, 800"), towards the pointer. On a face, the rectangle splits it.
/// </summary>
public sealed class RectangleTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click to place a corner.";
    private const string NextHint = "Click the opposite corner or type the size (1200, 800) · Esc stops";

    private Point3d? _corner;
    private Plane _plane;
    private Point3d _opposite;

    public override string Name => "Rectangle";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick);
        if (_corner is not null)
        {
            _opposite = OnPlane(_plane, Last, input.Pick);
        }
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Inference point = Infer(input.Pick);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_corner is not { } corner)
        {
            _corner = point.Point;
            _plane = PlaneAt(point);
            _opposite = point.Point;
            MeasurementName = "Size";
            Hint = NextHint;
            Show();
            return;
        }
        _opposite = OnPlane(_plane, point, input.Pick);
        (double width, double height) = Sides(corner, _opposite);
        DrawSized(corner, width, height);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _corner is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    public override bool TypeMeasurement(string text)
    {
        if (_corner is not { } corner || !Measurements.TryParseLengths(text, out double width, out double height))
        {
            return false;
        }
        (double towardsU, double towardsV) = Sides(corner, _opposite);
        DrawSized(corner, towardsU < 0 ? -width : width, towardsV < 0 ? -height : height);
        return true;
    }

    // The signed lengths of the sides, along the two axes of the plane, from a corner to the opposite one.
    private (double Width, double Height) Sides(Point3d corner, Point3d opposite)
    {
        (Vector3d u, Vector3d v) = AxesOf(_plane);
        Vector3d diagonal = opposite - corner;
        return (diagonal.Dot(u), diagonal.Dot(v));
    }

    private Point3d[] Corners(Point3d corner, double width, double height)
    {
        (Vector3d u, Vector3d v) = AxesOf(_plane);
        return [corner, corner + u * width, corner + u * width + v * height, corner + v * height];
    }

    private void DrawSized(Point3d corner, double width, double height)
    {
        if (Math.Abs(width) < 1e-6 || Math.Abs(height) < 1e-6)
        {
            Host.Report("A rectangle needs two sides that are not zero.");
            return;
        }
        Point3d[] corners = Corners(corner, width, height);
        (bool drawn, _) = Draw("Rectangle", [.. Enumerable.Range(0, 4).Select(i => new LineCurve(corners[i], corners[(i + 1) % 4]))]);
        if (drawn)
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _corner = null;
        MeasurementName = "Size";
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
        if (_corner is { } corner)
        {
            (double width, double height) = Sides(corner, _opposite);
            Measurement = Measurements.Format(Math.Abs(width), Math.Abs(height));
            Point3d[] corners = Corners(corner, width, height);
            Host.ShowPreview(new ToolPreview(
                [.. Enumerable.Range(0, 4).Select(i => new PreviewLine(corners[i], corners[(i + 1) % 4]))], Last));
            return;
        }
        Host.ShowPreview(Last is { Kind: not InferenceKind.None } point ? new ToolPreview([], point) : ToolPreview.Empty);
    }
}
