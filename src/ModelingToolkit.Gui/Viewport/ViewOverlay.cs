using Avalonia.Media;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// What the tools draw over the scene while they work, such as the line being drawn, the point it snaps to and its
/// length: in front of the scene, never picked. Sizes are in device-independent units.
/// </summary>
public sealed record ViewOverlay(
    IReadOnlyList<OverlayLine> Lines, IReadOnlyList<OverlayMarker> Markers, IReadOnlyList<OverlayLabel> Labels)
{
    public static ViewOverlay Empty { get; } = new([], [], []);

    public bool IsEmpty => Lines.Count == 0 && Markers.Count == 0 && Labels.Count == 0;
}

/// <summary>A polyline in the coordinates of the scene, two points or more.</summary>
/// <param name="Dash">The length of the dashes and of the gaps between them; 0 for a solid line.</param>
public sealed record OverlayLine(IReadOnlyList<Point3d> Points, Color Color, double Width = 1.5, double Dash = 0);

public enum MarkerShape
{
    /// <summary>A filled disc.</summary>
    Dot,

    /// <summary>A ring.</summary>
    Circle,

    /// <summary>An X.</summary>
    Cross,

    /// <summary>A +.</summary>
    Plus,
}

public sealed record OverlayMarker(Point3d Point, Color Color, MarkerShape Shape);

/// <summary>A text by a point, on a background of its own colour.</summary>
public sealed record OverlayLabel(Point3d Point, string Text, Color Color, Color Background);
