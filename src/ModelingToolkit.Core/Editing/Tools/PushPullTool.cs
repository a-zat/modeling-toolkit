using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Pushes and pulls faces: a click on a flat face of the geometry being edited takes it, the pointer then moves it
/// along its normal, to the point it snaps to or along the line of the normal, and a second click, or a distance typed
/// towards the pointer, pushes or pulls it there with <see cref="IGeometryKernel.PushPull"/>. The faces it makes take
/// the material of the face.
/// </summary>
public sealed class PushPullTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click a face to push or pull it.";
    private const string NextHint = "Click to end or type the distance · Esc stops";

    private Taken? _taken;
    private double _distance;
    private IReadOnlyList<PreviewLine> _hovered = [];

    public override string Name => "Push/Pull";

    /// <summary>
    /// The face taken, with where it was clicked, its outline and its corners, in the model space, and its normal
    /// towards the viewer: the side of the face the kernel pushes and pulls along (Sign 1), or the other (-1).
    /// </summary>
    private sealed record Taken(
        BrepEntity Entity, int Face, Point3d Base, Vector3d Normal, double Sign, IReadOnlyList<PreviewLine> Outline,
        IReadOnlyList<Point3d> Corners);

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_taken is { } taken)
        {
            _distance = DistanceTo(taken, input.Pick);
        }
        else
        {
            Last = Infer(input.Pick);
            _hovered = Take(input.Pick)?.Outline ?? [];
        }
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_taken is not { } taken)
        {
            if (Take(input.Pick) is not { } face)
            {
                return;
            }
            _taken = face;
            _distance = 0;
            Hint = NextHint;
            Show();
            return;
        }
        PushPull(taken, DistanceTo(taken, input.Pick));
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _taken is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    /// <summary>A distance typed: towards the pointer, or the other way when it starts with a minus.</summary>
    public override bool TypeMeasurement(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string value = text.Trim();
        bool reversed = value.StartsWith('-');
        if (_taken is not { } taken || !Measurements.TryParseLength(reversed ? value[1..] : value, out double length))
        {
            return false;
        }
        double towards = _distance < 0 ? -1 : 1;
        PushPull(taken, (reversed ? -towards : towards) * length);
        return true;
    }

    // The flat face of the geometry being edited under the pointer; null for none.
    private Taken? Take(ToolPick pick)
    {
        if (FlatFaceUnder(pick) is not { } face)
        {
            return null;
        }
        Vector3d normal = face.Plane.Normal;
        // The face of a solid is seen from outside, along its normal; one alone may be seen from either side.
        double sign = pick.Ray is { } ray && ray.Direction.Dot(normal) > 0 ? -1 : 1;
        return new Taken(face.Entity, face.Index, pick.Point, normal * sign, sign, face.Outline, face.Corners);
    }

    // The distance along the normal to the point under the pointer: the one it snaps to, or the nearest of the line of
    // the normal through the point clicked.
    private double DistanceTo(Taken taken, ToolPick pick)
    {
        Inference snapped = Infer(pick);
        if (snapped.Kind is InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.OnEdge)
        {
            Last = snapped;
        }
        else
        {
            Inference along = Infer(pick, taken.Base, taken.Normal);
            Last = along.Direction is null ? along with { Point = pick.Point } : along;
        }
        return (Last.Point - taken.Base).Dot(taken.Normal);
    }

    private void PushPull(Taken taken, double distance)
    {
        if (Math.Abs(distance) < 1e-6)
        {
            return;
        }
        try
        {
            ShapeEdit edit = Kernel.PushPull(taken.Entity.Shape, taken.Face, distance * taken.Sign);
            Host.Session.Execute(new ReplaceShapeCommand(Name, taken.Entity, edit));
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The face could not be pushed or pulled: {exception.Message}");
            return;
        }
        Restart();
    }

    protected override void Restart()
    {
        _taken = null;
        _distance = 0;
        _hovered = [];
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
        if (_taken is { } taken)
        {
            Measurement = Measurements.Format(_distance);
            Vector3d offset = taken.Normal * _distance;
            var lines = new List<PreviewLine>(taken.Outline);
            lines.AddRange(taken.Outline.Select(line => new PreviewLine(line.Start + offset, line.End + offset)));
            lines.AddRange(taken.Corners.Select(corner => new PreviewLine(corner, corner + offset)));
            Host.ShowPreview(new ToolPreview(lines, Last));
            return;
        }
        Host.ShowPreview(new ToolPreview(_hovered, Last is { Kind: not InferenceKind.None } point ? point : null));
    }
}
