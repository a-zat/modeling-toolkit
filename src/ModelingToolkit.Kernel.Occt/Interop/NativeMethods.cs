using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>P/Invoke declarations of the C API in native/occt-bridge/include/mtk/occt_bridge.h.</summary>
internal static partial class NativeMethods
{
    /// <summary>Must match MTK_OCCT_BRIDGE_ABI_VERSION in the native header.</summary>
    public const int AbiVersion = 2;

    private const string LibraryName = "mtk_occt_bridge";

    [LibraryImport(LibraryName, EntryPoint = "mtk_bridge_abi_version")]
    public static partial int BridgeAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_box")]
    public static partial NativeStatus ShapeMakeBox(double dx, double dy, double dz, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_volume")]
    public static partial NativeStatus ShapeVolume(ShapeHandle shape, out double volume);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_release")]
    public static partial void ShapeRelease(nint shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_read_gltf", StringMarshalling = StringMarshalling.Utf8)]
    public static partial NativeStatus SceneReadGltf(string path, out SceneHandle scene);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_release")]
    public static partial void SceneRelease(nint scene);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_info")]
    public static partial NativeStatus SceneGetInfo(SceneHandle scene, out SceneInfo info);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_block")]
    public static partial NativeStatus SceneGetBlock(SceneHandle scene, int index, out BlockInfo block);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_mesh")]
    public static partial NativeStatus SceneGetMesh(SceneHandle scene, int index, out MeshInfo mesh);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_reference")]
    public static partial NativeStatus SceneGetReference(SceneHandle scene, int index, out ReferenceInfo reference);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_material")]
    public static partial NativeStatus SceneGetMaterial(SceneHandle scene, int index, out MaterialInfo material);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_texture")]
    public static partial NativeStatus SceneGetTexture(SceneHandle scene, int index, out TextureInfo texture);

    [LibraryImport(LibraryName, EntryPoint = "mtk_scene_get_warning")]
    public static partial NativeStatus SceneGetWarning(SceneHandle scene, int index, out nint message);

    /// <summary>Copies a UTF-8 string owned by the bridge.</summary>
    public static string ToManagedString(nint utf8) => Marshal.PtrToStringUTF8(utf8) ?? string.Empty;

    // The bridge owns the strings it returns: they are declared as pointers and copied, because a string
    // return type would make the marshaller free them.
    public static string OcctVersion() => ToManagedString(OcctVersionPointer());

    public static string LastErrorMessage() => ToManagedString(LastErrorMessagePointer());

    [LibraryImport(LibraryName, EntryPoint = "mtk_occt_version")]
    private static partial nint OcctVersionPointer();

    [LibraryImport(LibraryName, EntryPoint = "mtk_last_error_message")]
    private static partial nint LastErrorMessagePointer();
}
