using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>P/Invoke declarations of the C API in native/occt-bridge/include/mtk/occt_bridge.h.</summary>
internal static partial class NativeMethods
{
    /// <summary>Must match MTK_OCCT_BRIDGE_ABI_VERSION in the native header.</summary>
    public const int AbiVersion = 1;

    private const string LibraryName = "mtk_occt_bridge";

    [LibraryImport(LibraryName, EntryPoint = "mtk_bridge_abi_version")]
    public static partial int BridgeAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_make_box")]
    public static partial NativeStatus ShapeMakeBox(double dx, double dy, double dz, out ShapeHandle shape);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_volume")]
    public static partial NativeStatus ShapeVolume(ShapeHandle shape, out double volume);

    [LibraryImport(LibraryName, EntryPoint = "mtk_shape_release")]
    public static partial void ShapeRelease(nint shape);

    // The bridge owns the strings it returns: they are declared as pointers and copied, because a string
    // return type would make the marshaller free them.
    public static string OcctVersion() => Marshal.PtrToStringUTF8(OcctVersionPointer()) ?? string.Empty;

    public static string LastErrorMessage() => Marshal.PtrToStringUTF8(LastErrorMessagePointer()) ?? string.Empty;

    [LibraryImport(LibraryName, EntryPoint = "mtk_occt_version")]
    private static partial nint OcctVersionPointer();

    [LibraryImport(LibraryName, EntryPoint = "mtk_last_error_message")]
    private static partial nint LastErrorMessagePointer();
}
