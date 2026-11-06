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

/// <summary>How the 3D view draws the meshes.</summary>
public enum DisplayMode
{
    Shaded,
    ShadedWithEdges,
    Wireframe,
}
