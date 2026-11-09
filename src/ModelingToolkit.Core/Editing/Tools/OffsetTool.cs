using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Offsets the outer loop of a flat face of the geometry being edited with <see cref="IGeometryKernel.OffsetFace"/>: a
/// click takes the face, the pointer then sets the distance, from the outline to the pointer on the plane of the face,
/// inwards when the pointer is on the face, and a second click, or a distance typed, offsets it.
/// </summary>
public sealed class OffsetTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click a face to offset its outline.";
    private const string NextHint = "Click to end or type the distance · Esc stops";

    private FlatFace? _face;
    private IReadOnlyList<PreviewLine> _hovered = [];
    private double _distance; // signed: inwards negative
    private PreviewLine? _gap;

    public override string Name => "Offset";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick);
        if (_face is { } face)
        {
            Measure(face, input.Pick);
        }
        else
        {
            _hovered = FlatFaceUnder(input.Pick)?.Outline ?? [];
        }
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Last = Infer(input.Pick);
        if (_face is not { } face)
        {
            if (FlatFaceUnder(input.Pick) is { } taken)
            {
                _face = taken;
                _distance = 0;
                _gap = null;
                Hint = NextHint;
                Show();
            }
            return;
        }
        Measure(face, input.Pick);
        OffsetBy(face, _distance);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _face is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    /// <summary>A distance typed: to the side of the pointer, or the other side when it starts with a minus.</summary>
    public override bool TypeMeasurement(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string value = text.Trim();
        bool reversed = value.StartsWith('-');
        if (_face is not { } face || !Measurements.TryParseLength(reversed ? value[1..] : value, out double length))
        {
            return false;
        }
        double side = _distance < 0 ? -1 : 1;
        OffsetBy(face, (reversed ? -side : side) * length);
        return true;
    }

    // The distance of the pointer, on the plane of the face, from its outline: negative inside it.
    private void Measure(FlatFace face, ToolPick pick)
    {
        if (Last is not { Kind: not InferenceKind.None } last)
        {
            return;
        }
        Point3d point = OnPlane(face.Plane, last, pick);
        double nearest = double.MaxValue;
        Point3d closest = point;
        foreach (PreviewLine line in face.Outline)
        {
            Point3d on = Closest(line, point);
            double distance = on.DistanceTo(point);
            if (distance < nearest)
            {
                nearest = distance;
                closest = on;
            }
        }
        _distance = Inside(face, point) ? -nearest : nearest;
        _gap = new PreviewLine(closest, point);
    }

    private static Point3d Closest(PreviewLine line, Point3d point)
    {
        Vector3d along = line.End - line.Start;
        double squared = along.Dot(along);
        double t = squared == 0 ? 0 : Math.Clamp((point - line.Start).Dot(along) / squared, 0, 1);
        return line.Start + along * t;
    }

    // Whether a point of the plane is inside the outline: the segments that a ray along the plane crosses, an odd count.
    private bool Inside(FlatFace face, Point3d point)
    {
        (Vector3d u, Vector3d v) = AxesOf(face.Plane);
        (double X, double Y) Flat(Point3d p) => ((p - point).Dot(u), (p - point).Dot(v));
        bool inside = false;
        foreach (PreviewLine line in face.Outline)
        {
            (double ax, double ay) = Flat(line.Start);
            (double bx, double by) = Flat(line.End);
            if ((ay > 0) != (by > 0) && ax + (bx - ax) * (0 - ay) / (by - ay) > 0)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private void OffsetBy(FlatFace face, double distance)
    {
        if (Math.Abs(distance) < 1e-6)
        {
            return;
        }
        try
        {
            ShapeEdit edit = Kernel.OffsetFace(face.Entity.Shape, face.Index, distance);
            Host.Session.Execute(new ReplaceShapeCommand(Name, face.Entity, edit));
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The face could not be offset: {exception.Message}");
            return;
        }
        Restart();
    }

    protected override void Restart()
    {
        _face = null;
        _hovered = [];
        _distance = 0;
        _gap = null;
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
        Inference? point = Last is { Kind: not InferenceKind.None } last ? last : null;
        if (_face is { } face)
        {
            Measurement = Measurements.Format(Math.Abs(_distance));
            Host.ShowPreview(new ToolPreview(_gap is { } gap ? [.. face.Outline, gap] : face.Outline, point));
            return;
        }
        Host.ShowPreview(new ToolPreview(_hovered, point));
    }
}
