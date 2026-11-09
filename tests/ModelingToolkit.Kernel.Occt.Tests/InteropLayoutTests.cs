using System.Runtime.CompilerServices;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt.Tests;

// Same sizes as the static_asserts in native/occt-bridge/src/scene.cpp and edges.cpp: a mismatch would corrupt memory.
public sealed class InteropLayoutTests
{
    [Fact]
    public void Scene_structures_have_the_native_sizes()
    {
        Assert.Equal(24, Unsafe.SizeOf<SceneInfo>());
        Assert.Equal(24, Unsafe.SizeOf<BlockInfo>());
        Assert.Equal(48, Unsafe.SizeOf<MeshInfo>());
        Assert.Equal(112, Unsafe.SizeOf<ReferenceInfo>());
        Assert.Equal(40, Unsafe.SizeOf<MaterialInfo>());
        Assert.Equal(32, Unsafe.SizeOf<TextureInfo>());
    }

    [Fact]
    public void Edit_structures_have_the_native_sizes()
    {
        Assert.Equal(NativeCurve.Size, Unsafe.SizeOf<NativeCurve>());
        Assert.Equal(NativeEdgeInfo.Size, Unsafe.SizeOf<NativeEdgeInfo>());
    }
}
