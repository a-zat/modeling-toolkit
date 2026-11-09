namespace ModelingToolkit.Gui.Viewport;

/// <summary>The views of the faces of the ViewCube, and the initial view, from its front right top corner.</summary>
// The values are those of mtk_viewer_view and mtk_viewer_display_mode in the native viewer.
public enum StandardView
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right,
    Isometric,
}

/// <summary>How the 3D view shades the faces, in the display modes that draw them.</summary>
// The values are those of mtk_viewer_render_style in the native viewer.
public enum RenderStyle
{
    /// <summary>Lit by the materials, with their highlights.</summary>
    Standard,

    /// <summary>
    /// As in technical drawings: on white, flat shades without highlights, lit from the upper left, with thick dark
    /// outlines in every display mode but the wireframe.
    /// </summary>
    Cad,
}

/// <summary>How the 3D view draws the meshes.</summary>
public enum DisplayMode
{
    Shaded,
    ShadedWithEdges,
    Wireframe,
}
