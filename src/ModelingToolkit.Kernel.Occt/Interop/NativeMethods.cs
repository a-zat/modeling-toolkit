using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>P/Invoke declarations of the C API in native/occt-bridge/include/mtk/occt_bridge.h.</summary>
internal static partial class NativeMethods
{
    /// <summary>Must match MTK_OCCT_BRIDGE_ABI_VERSION in the native header.</summary>
    public const int AbiVersion = 6;

    private const string LibraryName = "mtk_occt_bridge";

    [LibraryImport(LibraryName, EntryPoint = "mtk_bridge_abi_version")]
    public static partial int BridgeAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_box")]
    public static partial NativeStatus ShapeMakeBox(double dx, double dy, double dz, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_cylinder")]
    public static partial NativeStatus ShapeMakeCylinder(double radius, double height, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_sphere")]
    public static partial NativeStatus ShapeMakeSphere(double radius, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_cone")]
    public static partial NativeStatus ShapeMakeCone(
        double bottomRadius, double topRadius, double height, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_torus")]
    public static partial NativeStatus ShapeMakeTorus(double majorRadius, double minorRadius, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_place")]
    public static partial NativeStatus ShapePlace(
        ShapeHandle shape, ReadOnlySpan<double> placement, out ShapeHandle placed);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_boolean")]
    public static partial NativeStatus ShapeBoolean(
        ShapeHandle first, ShapeHandle second, int operation, out ShapeHandle result);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_volume")]
    public static partial NativeStatus ShapeVolume(ShapeHandle shape, out double volume);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_bounds")]
    public static partial NativeStatus ShapeBounds(ShapeHandle shape, Span<double> bounds);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_release")]
    public static partial void ShapeRelease(nint shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_write")]
    public static partial NativeStatus ShapeWrite(ShapeHandle shape, out BytesHandle bytes);

    [LibraryImport(LibraryName, EntryPoint = "mtk_bytes_get")]
    public static unsafe partial NativeStatus BytesGet(BytesHandle bytes, out byte* data, out long size);

    [LibraryImport(LibraryName, EntryPoint = "mtk_bytes_release")]
    public static partial void BytesRelease(nint bytes);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_read")]
    public static partial NativeStatus ShapeRead(ReadOnlySpan<byte> data, long size, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_tessellate")]
    public static partial NativeStatus ShapeTessellate(
        ShapeHandle shape, double linearDeflection, double angularDeflection, out TessellationHandle tessellation);

    [LibraryImport(LibraryName, EntryPoint = "mtk_tessellation_get_mesh")]
    public static partial NativeStatus TessellationGetMesh(TessellationHandle tessellation, out MeshInfo mesh);

    [LibraryImport(LibraryName, EntryPoint = "mtk_tessellation_release")]
    public static partial void TessellationRelease(nint tessellation);

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
