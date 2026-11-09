using Avalonia.Media;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// What a tool shows, as the 3D view draws it: lines in the colour of what they follow (the red, green and blue axes,
/// magenta for parallels and perpendiculars), and the point inferred with a marker and the name of its kind.
/// </summary>
internal static class ToolOverlay
{
    private static readonly Color Red = Color.FromRgb(0xD8, 0x32, 0x2A);
    private static readonly Color Green = Color.FromRgb(0x2E, 0x9E, 0x44);
    private static readonly Color Blue = Color.FromRgb(0x2F, 0x6F, 0xDE);
    private static readonly Color Magenta = Color.FromRgb(0xC2, 0x38, 0xC2);
    private static readonly Color Cyan = Color.FromRgb(0x14, 0xA3, 0xB8);
    private static readonly Color Gold = Color.FromRgb(0xE0, 0xA8, 0x16);
    private const double GuideReach = 100_000;
    private const int GuidePieces = 100;

    /// <param name="dark">Whether the theme is dark.</param>
    /// <param name="cad">Whether the view draws in the CAD render style, on white.</param>
    /// <param name="guides">The guide lines to show, dashed and endless (100 m each way).</param>
    public static ViewOverlay Of(ToolPreview preview, bool dark, bool cad, IReadOnlyList<GuideLine>? guides = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        // A plain line looks as the edge it will be: as Mtk.Viewport.Edge, one pixel wide, or as the outlines of the
        // CAD style (Mtk.Viewport.Outline, 1.5 wide); lines along an axis or an edge stand out, wider, in its colour.
        (Color plain, double width) = cad ? (Color.FromRgb(0x16, 0x18, 0x1C), 1.5)
            : dark ? (Color.FromRgb(0x7D, 0x86, 0x93), 1.0) : (Color.FromRgb(0x4B, 0x55, 0x63), 1.0);
        List<OverlayLine> lines = [.. preview.Lines.Select(line => ColorOf(line.Along) is { } color
            ? new OverlayLine([line.Start, line.End], color, 2)
            : new OverlayLine([line.Start, line.End], plain, width))];
        Color guideColor = dark ? Color.FromRgb(0x9A, 0xA3, 0xB0) : Color.FromRgb(0x5B, 0x64, 0x72);
        foreach (GuideLine guide in guides ?? [])
        {
            // In pieces, so that none reaches behind the camera, where its dashes would be lost.
            Point3d[] points = [.. Enumerable.Range(-GuidePieces, 2 * GuidePieces + 1)
                .Select(i => guide.Point + guide.Direction * (GuideReach * i / GuidePieces))];
            lines.Add(new OverlayLine(points, guideColor, 1, 4));
        }
        List<OverlayMarker> markers = [];
        List<OverlayLabel> labels = [];
        if (preview.Inference is { } inference && MarkerOf(inference.Kind) is { } marker)
        {
            markers.Add(new OverlayMarker(inference.Point, marker.Color, marker.Shape));
            labels.Add(new OverlayLabel(inference.Point, $"  {NameOf(inference.Kind)}", Color.FromRgb(0x1C, 0x1F, 0x24),
                Color.FromRgb(0xFF, 0xFF, 0xFF)));
        }
        return new ViewOverlay(lines, markers, labels);
    }

    /// <summary>The name of a kind of point, as the 3D view shows it next to the point.</summary>
    public static string NameOf(InferenceKind kind) => kind switch
    {
        InferenceKind.Endpoint => "Endpoint",
        InferenceKind.Midpoint => "Midpoint",
        InferenceKind.OnEdge => "On edge",
        InferenceKind.OnFace => "On face",
        InferenceKind.Origin => "Origin",
        InferenceKind.AxisX => "On red axis",
        InferenceKind.AxisY => "On green axis",
        InferenceKind.AxisZ => "On blue axis",
        InferenceKind.Parallel => "Parallel to edge",
        InferenceKind.Perpendicular => "Perpendicular to edge",
        InferenceKind.OnGuide => "On guide",
        InferenceKind.Intersection => "Intersection",
        _ => string.Empty,
    };

    private static Color? ColorOf(InferenceKind along) => along switch
    {
        InferenceKind.AxisX => Red,
        InferenceKind.AxisY => Green,
        InferenceKind.AxisZ => Blue,
        InferenceKind.Parallel or InferenceKind.Perpendicular => Magenta,
        _ => null,
    };

    private static (Color Color, MarkerShape Shape)? MarkerOf(InferenceKind kind) => kind switch
    {
        InferenceKind.Endpoint => (Green, MarkerShape.Dot),
        InferenceKind.Midpoint => (Cyan, MarkerShape.Dot),
        InferenceKind.OnEdge => (Red, MarkerShape.Dot),
        InferenceKind.OnFace => (Blue, MarkerShape.Circle),
        InferenceKind.Origin => (Gold, MarkerShape.Dot),
        InferenceKind.OnGuide => (Gold, MarkerShape.Dot),
        InferenceKind.Intersection => (Green, MarkerShape.Dot),
        InferenceKind.AxisX or InferenceKind.AxisY or InferenceKind.AxisZ or InferenceKind.Parallel
            or InferenceKind.Perpendicular => (ColorOf(kind)!.Value, MarkerShape.Circle),
        _ => null, // on the ground, nothing to point out
    };
}
