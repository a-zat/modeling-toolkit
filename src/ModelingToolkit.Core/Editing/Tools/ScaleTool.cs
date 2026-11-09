using System.Globalization;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Scales an entity of the block being edited, the one selected or else the one clicked, the same in every direction:
/// a click sets the point that stays, a second a point of reference, and a third where that point goes, the factor
/// being the ratio of their distances along the line from the point that stays. A factor can be typed ("2", "0.5"), or
/// a length with its unit ("500mm") for the new distance of the reference. With Option (Alt) held, a copy is scaled.
/// </summary>
public sealed class ScaleTool(IGeometryKernel kernel) : TransformTool(kernel)
{
    private const string BaseHint = "Click the point that stays";
    private const string ReferenceHint = "Click a point of reference, or type the factor";
    private const string FactorHint = "Click where the point goes or type the factor or a length · ⌥ copies";

    private Point3d? _base;
    private Point3d? _reference;
    private double _factor = 1;

    public override string Name => "Scale";

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
        if (_base is null)
        {
            if (TakeTarget(input.Pick))
            {
                _base = point.Point;
                Hint = ReferenceHint;
                Show();
            }
            return;
        }
        if (_reference is null)
        {
            if (point.Point.DistanceTo(_base.Value) > 1e-6)
            {
                _reference = point.Point;
                _factor = 1;
                Hint = FactorHint;
                Show();
            }
            return;
        }
        ScaleBy(_factor);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        if (key == ToolKey.Escape && _base is not null)
        {
            Restart();
            return true;
        }
        return false;
    }

    /// <summary>A factor typed, or a length with its unit for the new distance of the point of reference.</summary>
    public override bool TypeMeasurement(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_base is not { } origin)
        {
            return false;
        }
        string value = text.Trim().Replace(',', '.');
        double factor;
        if (value.Length > 0 && char.IsDigit(value[^1]))
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out factor))
            {
                return false;
            }
        }
        else if (_reference is { } reference && Measurements.TryParseLength(value, out double length))
        {
            factor = length / origin.DistanceTo(reference);
        }
        else
        {
            return false;
        }
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return false;
        }
        ScaleBy(factor);
        return true;
    }

    private void Track(ToolPick pick)
    {
        Last = Infer(pick, _base);
        if (_base is { } origin && _reference is { } reference && Last.Kind != InferenceKind.None)
        {
            Vector3d along = reference - origin;
            _factor = (Last.Point - origin).Dot(along) / along.Dot(along);
        }
    }

    private void ScaleBy(double factor)
    {
        if (_base is not { } origin)
        {
            return;
        }
        if (factor <= 1e-9 || !double.IsFinite(factor))
        {
            Host.Report("The factor of a scaling must be positive.");
            return;
        }
        if (Math.Abs(factor - 1) < 1e-12 || Apply(Transform.Scaling(origin, factor)))
        {
            Restart();
        }
    }

    protected override void Restart()
    {
        _base = null;
        _reference = null;
        _factor = 1;
        ReleaseTarget();
        MeasurementName = "Scale";
        Hint = BaseHint;
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
        var lines = new List<PreviewLine>();
        if (_base is { } origin && _reference is { } reference)
        {
            Measurement = Math.Round(_factor, 3).ToString("0.###", CultureInfo.InvariantCulture);
            lines.Add(new PreviewLine(origin, reference));
            if (point is not null)
            {
                lines.Add(new PreviewLine(origin, point.Point, point.Direction is null ? InferenceKind.None : point.Kind));
            }
            if (_factor > 1e-9)
            {
                lines.AddRange(Transformed(Box, Transform.Scaling(origin, _factor)));
            }
        }
        else if (_base is { } start)
        {
            Measurement = "1";
            lines.AddRange(Box);
            if (point is not null)
            {
                lines.Add(new PreviewLine(start, point.Point));
            }
        }
        Host.ShowPreview(new ToolPreview(lines, point));
    }
}
