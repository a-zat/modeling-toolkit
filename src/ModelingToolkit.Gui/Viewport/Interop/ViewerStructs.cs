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

/// <summary>Colours of the view, as sRGB components from 0 to 1.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ViewerColors
{
    public fixed float BackgroundTop[3];
    public fixed float BackgroundBottom[3];
    public fixed float Foreground[3];
}
