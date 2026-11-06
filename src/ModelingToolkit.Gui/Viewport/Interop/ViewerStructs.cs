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
    public fixed float Grid[3];
    public fixed float AxisX[3];
    public fixed float AxisY[3];
    public fixed float AxisZ[3];
    public fixed float CubeFace[3];
    public fixed float CubeEdge[3];
    public fixed float CubeText[3];
    public fixed float Highlight[3];
    public fixed float Edges[3];
}

/// <summary>A triangle mesh; the arrays are read during the call only.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeMesh
{
    public double* Positions;
    public float* Normals;
    public float* Uvs;
    public int VertexCount;
    public int* Triangles;
    public int TriangleCount;
    public double* EdgePoints;
    public int* EdgeFirstPoints;
    public int EdgeCount;
}

/// <summary>A material of the metallic-roughness model of glTF.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeMaterial
{
    public fixed float BaseColor[4];
    public float Metallic;
    public float Roughness;
    public int BaseColorTexture;
}
