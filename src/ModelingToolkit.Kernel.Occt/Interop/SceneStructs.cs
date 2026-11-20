using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

// Mirrors of the mtk_scene_* structures in occt_bridge.h. Field order and types must match the C declarations;
// the native static_asserts and the managed tests check the sizes on both sides.

[StructLayout(LayoutKind.Sequential)]
internal struct SceneInfo
{
    public int BlockCount;
    public int MeshCount;
    public int ReferenceCount;
    public int MaterialCount;
    public int TextureCount;
    public int WarningCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BlockInfo
{
    public nint Name;
    public int FirstMesh;
    public int MeshCount;
    public int FirstReference;
    public int ReferenceCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MeshInfo
{
    public int Material;
    public int VertexCount;
    public int TriangleCount;
    public double* Vertices;
    public float* Normals;
    public float* TextureCoordinates;
    public int* Triangles;
    public nint Name;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ReferenceInfo
{
    public nint Name;
    public int Block;
    public fixed double Transform[12];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MaterialInfo
{
    public nint Name;
    public fixed float BaseColor[4];
    public float Metallic;
    public float Roughness;
    public int BaseColorTexture;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TextureInfo
{
    public nint Name;
    public nint MediaType;
    public byte* Data;
    public long Size;
}
