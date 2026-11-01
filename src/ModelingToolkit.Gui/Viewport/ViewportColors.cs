using Avalonia.Media;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// Colours of the 3D view and of its navigation aids, from the resources of the theme. The opacity of the ViewCube is
/// the alpha of the colour of its faces.
/// </summary>
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
    Color Highlight);
