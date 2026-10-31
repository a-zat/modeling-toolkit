using System.Runtime.InteropServices;

namespace ModelingToolkit.Gui.Viewport.Interop;

// Mirrors of the types in native/occt-viewer/include/mtk/occt_viewer.h.

[Flags]
internal enum NativeMouseButtons : uint
{
    None = 0,
    Left = 1,
    Middle = 2,
    Right = 4,
}

[Flags]
internal enum NativeModifierKeys : uint
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>Colours of the view and of its navigation aids, as sRGB components from 0 to 1.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ViewerColors
{
    public fixed float BackgroundTop[3];
    public fixed float BackgroundBottom[3];
    public fixed float AxisX[3];
    public fixed float AxisY[3];
    public fixed float AxisZ[3];
    public fixed float CubeFace[3];
    public fixed float CubeEdge[3];
    public fixed float CubeText[3];
    public float CubeTransparency;
    public fixed float Highlight[3];
}
