using Avalonia.Media;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>Colours of the 3D view and of its navigation aids, from the resources of the theme.</summary>
internal sealed record ViewportColors(
    Color BackgroundTop,
    Color BackgroundBottom,
    Color Grid,
    Color AxisX,
    Color AxisY,
    Color AxisZ,
    Color CubeFace,
    Color CubeEdge,
    Color CubeText,
    Color Highlight,
    Color Edges,
    Color Outlines);
