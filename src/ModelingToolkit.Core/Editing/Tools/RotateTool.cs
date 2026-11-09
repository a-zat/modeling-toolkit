using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Rotates an entity of the block being edited, the one selected or else the one clicked, with a protractor: it lies on
/// the face under the pointer or on the ground, or across the axis an arrow key chooses; a click sets the centre, a
/// second the direction the angle starts from, and a third the angle, which snaps to steps of 15°, or an angle typed in
/// degrees towards the pointer. With Option (Alt) held, a copy is rotated and the entity stays.
/// </summary>
public sealed class RotateTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string CentreHint = "Click the centre of the rotation · arrows choose the axis";
    private const string FromHint = "Click where the angle starts";
    private const string AngleHint = "Click the angle or type it in degrees · ⌥ copies · Esc stops";
    private const double Step = 15;

    private Point3d? _centre;
    private Plane _plane;
    private Vector3d? _from;
    private double _angle; // radians, counterclockwise around the normal of the plane
    private Vector3d? _axis; // chosen by an arrow key
    private double _radius = 100;
    private Point3d _pointer;

    public override string Name => "Rotate";

    public override void Hover(ToolInput input)
    {
        Follow(input);
        Track(input.Pick);
        Show();
    }

    public override void Click(ToolInput input)
    {
        Follow(input);
        Track(input.Pick);
        if (Last is not { Kind: not InferenceKind.None } point)
        {
            return;
        }
        if (_centre is null)
        {
            if (TakeTarget(input.Pick))
            {
                _centre = point.Point;
                _plane = PlaneThrough(point);
                Hint = FromHint;
                Show();
            }
            return;
        }
        if (_from is null)
        {
            Vector3d from = _pointer - _centre.Value;
            if (from.Length > 1e-6)
            {
                _from = from.Normalized();
                _angle = 0;
                Hint = AngleHint;
                Show();
            }
            return;
        }
        RotateBy(_angle);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _centre is not null)
        {
            Restart();
            return true;
        }
        if (_centre is null && key is ToolKey.Right or ToolKey.Left or ToolKey.Up)
        {
            Vector3d axis = Host.Context.Placement.ApplyVector(
                key == ToolKey.Right ? Vector3d.UnitX : key == ToolKey.Left ? Vector3d.UnitY : Vector3d.UnitZ).Normalized();
            _axis = _axis is { } chosen && (chosen - axis).Length < 1e-9 ? null : axis;
            return true;
        }
        return false;
    }

    /// <summary>An angle typed, in degrees: towards the pointer, or the other way when negative.</summary>
    public override bool TypeMeasurement(string text)
    {
        if (_from is null || !Measurements.TryParseAngle(text, out double degrees))
        {
            return false;
        }
        RotateBy((_angle < 0 ? -degrees : degrees) * Math.PI / 180);
        return true;
    }

    // Infers the point under the pointer, and the angle to it once the protractor is set.
    private void Track(ToolPick pick)
    {
        Inference point = Infer(pick);
        Last = point;
        if (point.Kind == InferenceKind.None)
        {
            return;
        }
        if (_centre is not { } centre)
        {
            _plane = PlaneThrough(point);
            _radius = Radius(pick, point.Point);
            return;
        }
        _pointer = OnPlane(_plane, point, pick);
        if (_from is not { } from)
        {
            return;
        }
        Vector3d to = _pointer - centre;
        if (to.Length < 1e-9)
        {
            return;
        }
        double degrees = Math.Atan2(from.Cross(to).Dot(_plane.Normal), from.Dot(to)) * 180 / Math.PI;
        double snapped = Math.Round(degrees / Step) * Step;
        _angle = (Math.Abs(degrees - snapped) < 2 ? snapped : degrees) * Math.PI / 180;
    }

    private Plane PlaneThrough(Inference point)
    {
        Vector3d normal = _axis ?? PlaneAt(point).Normal;
        return new Plane(normal, normal.Dot(point.Point - Point3d.Origin));
    }

    private static double Radius(ToolPick pick, Point3d point) => pick.Ray is { } ray ? 40 * ray.UnitSizeAt(point) : 100;

    private void RotateBy(double angle)
    {
        if (_centre is not { } centre || Math.Abs(angle) < 1e-9)
        {
            return;
        }
        if (Apply(Transform.Rotation(centre, _plane.Normal, angle)))
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _centre = null;
        _from = null;
        _angle = 0;
        ReleaseTarget();
        MeasurementName = "Angle";
        Hint = CentreHint;
        Measurement = null;
        Show();
    }

    protected override void Show()
    {
        if (!IsActive)
        {
            return;
        }
        Point3d? at = _centre ?? (Last is { Kind: not InferenceKind.None } point ? point.Point : null);
        if (at is not { } centre)
        {
            Host.ShowPreview(ToolPreview.Empty);
            return;
        }
        (Vector3d u, _) = AxesOf(_plane);
        var lines = new List<PreviewLine>(Around(centre, u * _radius, _plane.Normal, 2 * Math.PI));
        if (_from is { } from)
        {
            Transform rotation = Transform.Rotation(centre, _plane.Normal, _angle);
            Vector3d to = rotation.ApplyVector(from);
            lines.Add(new PreviewLine(centre, centre + from * _radius * 1.5));
            lines.Add(new PreviewLine(centre, centre + to * _radius * 1.5));
            lines.AddRange(Around(centre, from * _radius * 0.6, _plane.Normal, _angle, 24));
            lines.AddRange(Transformed(Box, rotation));
            Measurement = Measurements.FormatAngle(_angle * 180 / Math.PI);
        }
        else if (_centre is not null)
        {
            lines.Add(new PreviewLine(centre, _pointer));
        }
        Host.ShowPreview(new ToolPreview(lines, Last is { Kind: not InferenceKind.None } last ? last : null));
    }
}
