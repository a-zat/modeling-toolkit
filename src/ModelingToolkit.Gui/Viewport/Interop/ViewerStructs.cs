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
    public int* FaceFirstTriangles;
    public int FaceCount;
    public int* EdgeIndices;
    public double* ShapeVertices;
    public int ShapeVertexCount;
}

/// <summary>What a pick finds, as the viewer gives it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePick
{
    public int Instance;
    public NativeElementKind Kind;
    public int Index;
    public fixed double Point[3];
    public fixed float Normal[3];
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

/// <summary>What a pick finds: the whole instance, for a mesh picked as a whole, or an element of its shape.</summary>
internal enum NativeElementKind
{
    Instance = 0,
    Face = 1,
    Edge = 2,
    Vertex = 3,
}

/// <summary>An element of the shape that an instance of the viewer shows, numbered as in the shape.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct ViewerElement(int Instance, NativeElementKind Kind, int Index);
