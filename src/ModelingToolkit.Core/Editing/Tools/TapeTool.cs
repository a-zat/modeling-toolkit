using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Measures, and draws guides: from a point, a second click measures the distance to another, both inferred; from an
/// edge, a second click puts a guide line parallel to it through the point under the pointer, or at a distance typed,
/// in the block being edited.
/// </summary>
public sealed class TapeTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
    private const string StartHint = "Click a point to measure from, or an edge for a guide parallel to it";
    private const string MeasureHint = "Click the point to measure to · Esc stops";
    private const string GuideHint = "Click where the guide goes or type its distance · Esc stops";
    private const double PreviewReach = 50_000;

    private Point3d? _start;
    private Vector3d? _along; // the direction of the edge a guide is drawn from
    private Point3d _at; // where the guide goes
    private double _distance;

    public override string Name => "Tape Measure";

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Track(input.Pick);
        Show();
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Track(input.Pick);
        if (Last is not { Kind: not InferenceKind.None } point)
        {
            return;
        }
        if (_start is null)
        {
            _start = point.Point;
            _along = point.Kind is InferenceKind.OnEdge or InferenceKind.Midpoint ? Inference.Reference?.Normalized() : null;
            Hint = _along is null ? MeasureHint : GuideHint;
            Show();
            return;
        }
        if (_along is { } along)
        {
            AddGuide(_at, along);
            return;
        }
        Host.Report($"Length: {Measurements.Format(_distance)}");
        Restart();
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

    /// <summary>The distance of the guide from its edge, towards the pointer.</summary>
    public override bool TypeMeasurement(string text)
    {
        if (_start is not { } start || _along is not { } along || !Measurements.TryParseLength(text, out double length))
        {
            return false;
        }
        Vector3d across = (_at - start).Normalized();
        if (across.Length == 0)
        {
            return false;
        }
        AddGuide(start + across * length, along);
        return true;
    }

    private void Track(ToolPick pick)
    {
        Last = _start is { } start && _along is null ? Infer(pick, start) : Infer(pick);
        if (_start is not { } from || Last.Kind == InferenceKind.None)
        {
            return;
        }
        Vector3d to = Last.Point - from;
        if (_along is { } along)
        {
            Vector3d across = to - along * to.Dot(along);
            _at = from + across;
            _distance = across.Length;
        }
        else
        {
            _distance = to.Length;
        }
    }

    private void AddGuide(Point3d at, Vector3d along)
    {
        Transform toBlock = Host.Context.Placement.Inverse();
        Host.Session.Execute(new AddEntityCommand(
            "Guide", Host.Context.Block, new GuideEntity(toBlock.Apply(at), toBlock.ApplyVector(along))));
        Restart();
    }

    protected override void Restart()
    {
        _start = null;
        _along = null;
        _distance = 0;
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
        Inference? point = Last is { Kind: not InferenceKind.None } last ? last : null;
        if (_start is not { } start || point is null)
        {
            Measurement = null;
            Host.ShowPreview(new ToolPreview([], point));
            return;
        }
        Measurement = Measurements.Format(_distance);
        if (_along is { } along)
        {
            Host.ShowPreview(new ToolPreview(
                [new PreviewLine(start, _at), new PreviewLine(_at - along * PreviewReach, _at + along * PreviewReach)], point));
            return;
        }
        InferenceKind kind = point.Direction is null ? InferenceKind.None : point.Kind;
        Host.ShowPreview(new ToolPreview([new PreviewLine(start, point.Point, kind)], point));
    }
}
