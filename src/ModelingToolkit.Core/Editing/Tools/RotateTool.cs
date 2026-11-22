using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Rotates an entity of the block being edited, the one selected or else the one clicked, with a protractor: it lies on
/// the face under the pointer or on the ground, or across the axis an arrow key chooses; a click sets the centre, a
/// second the direction the angle starts from, and a third the angle, which snaps to right angles, and more closely to
/// steps of 15°, or an angle typed in degrees towards the pointer. With Option (Alt) held, a copy is rotated and the entity stays.
/// </summary>
public sealed class RotateTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string CentreHint = "Click the centre of the rotation · arrows or X Y Z choose the axis · ⇧ on a face takes its plane";
    private const string FromHint = "Click where the angle starts";
    private const string AngleHint = "Click the angle or type it in degrees · ⌥ copies · Esc stops";
    private const double Step = 15;
    private const double RightAngle = 90;

    /// <summary>How far, in degrees, an angle may be off a right angle and still snap to it.</summary>
    public const double RightAngleSnap = 7;

    private Point3d? _centre;
    private Plane _plane;
    private Vector3d? _from;
    private double _angle; // radians, counterclockwise around the normal of the plane
    private Vector3d? _axis; // chosen by an arrow key
    private double _radius = 100;
    private Point3d _pointer;
    private bool _onFace; // Shift held: on the plane of the face under the pointer

    public override string Name => "Rotate";

    public override void Hover(ToolInput input)
    {
        Follow(input);
        _onFace = input.Modifiers.HasFlag(ToolModifiers.Shift);
        Track(_centre is null ? input.Pick : Clear(input.Pick));
        Show();
    }

    public override void Click(ToolInput input)
    {
        Follow(input);
        _onFace = input.Modifiers.HasFlag(ToolModifiers.Shift);
        Track(_centre is null ? input.Pick : Clear(input.Pick));
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

    public override Point3d? TakenAt => _centre;

    /// <summary>Takes the selection to turn it around a point, as a click there would.</summary>
    public override bool Grab(Inference grip)
    {
        ArgumentNullException.ThrowIfNull(grip);
        Restart();
        if (!TakeTarget(ToolPick.Nothing))
        {
            return false;
        }
        Last = grip;
        _centre = grip.Point;
        _plane = PlaneThrough(grip);
        Hint = FromHint;
        Show();
        return true;
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
        double? heading = Target is { } target ? Heading(target, TargetPlacement, _plane.Normal) : null;
        _angle = Snap(Math.Atan2(from.Cross(to).Dot(_plane.Normal), from.Dot(to)) * 180 / Math.PI, heading) * Math.PI / 180;
    }

    /// <summary>
    /// An angle in degrees snapped to a right angle near it, or else to a step of 15° close to it. Given the heading of
    /// the entity (<see cref="Heading"/>), it also snaps to the angles that leave the entity square with the axes, the
    /// nearer of the two right angles winning: an entity turned by 32° turns by 148° to stand square again.
    /// </summary>
    public static double Snap(double degrees, double? heading = null)
    {
        double right = Math.Round(degrees / RightAngle) * RightAngle;
        double best = Math.Abs(degrees - right) < RightAngleSnap ? right : double.NaN;
        if (heading is { } h)
        {
            double square = Math.Round((h + degrees) / RightAngle) * RightAngle - h;
            if (Math.Abs(degrees - square) < RightAngleSnap &&
                (double.IsNaN(best) || Math.Abs(degrees - square) < Math.Abs(degrees - best)))
            {
                best = square;
            }
        }
        if (!double.IsNaN(best))
        {
            return best;
        }
        double step = Math.Round(degrees / Step) * Step;
        return Math.Abs(degrees - step) < 2 ? step : degrees;
    }

    /// <summary>
    /// How far, in degrees around an axis, a component is turned from the axes of the model: the angle from the axis of
    /// the model to that of the component nearest the plane across the axis, both laid on it; null for other entities,
    /// which have no axes of their own, or when either lies along the axis.
    /// </summary>
    public static double? Heading(Entity entity, Transform placement, Vector3d axis)
    {
        if (entity is not BlockReference reference || axis.Length < 1e-12)
        {
            return null;
        }
        Vector3d normal = axis.Normalized();
        Transform transform = placement * reference.Transform;
        Vector3d? own = Across(normal, [.. new[] { Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ }.Select(transform.ApplyVector)]);
        Vector3d? model = Across(normal, [Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ]);
        if (own is not { } a || model is not { } b)
        {
            return null;
        }
        return Math.Atan2(b.Cross(a).Dot(normal), b.Dot(a)) * 180 / Math.PI;

        // Of directions, the one that lies the most across the axis, laid on the plane across it.
        static Vector3d? Across(Vector3d normal, Vector3d[] directions)
        {
            Vector3d best = directions.Select(d => d.Normalized()).MinBy(d => Math.Abs(d.Dot(normal)));
            Vector3d laid = best - normal * best.Dot(normal);
            return laid.Length < 1e-6 ? null : laid.Normalized();
        }
    }

    private Plane PlaneThrough(Inference point)
    {
        // Around the vertical, the turn of furniture, unless an axis is chosen, a face held with Shift, or the middle of a
        // face of the box of the selection taken.
        Vector3d normal = _axis ?? (_onFace || point.Kind == InferenceKind.Centre
            ? PlaneAt(point).Normal
            : Host.Context.Placement.ApplyVector(Vector3d.UnitZ).Normalized());
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
            ShowTransformed(Copy ? null : rotation);
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
