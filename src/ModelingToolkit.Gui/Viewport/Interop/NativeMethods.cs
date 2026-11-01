using System.Runtime.InteropServices;

namespace ModelingToolkit.Gui.Viewport.Interop;

/// <summary>P/Invoke declarations of the C API in native/occt-viewer/include/mtk/occt_viewer.h.</summary>
internal static partial class NativeMethods
{
    /// <summary>Must match MTK_OCCT_VIEWER_ABI_VERSION in the native header.</summary>
    public const int AbiVersion = 3;

    private const string LibraryName = "mtk_occt_viewer";

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_abi_version")]
    public static partial int ViewerAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_create")]
    public static partial NativeStatus Create(nint parent, out ViewerHandle viewer);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_release")]
    public static partial void Release(nint viewer);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_get_native_view")]
    public static partial NativeStatus GetNativeView(ViewerHandle viewer, out nint view);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_colors")]
    public static partial NativeStatus SetColors(ViewerHandle viewer, in ViewerColors colors);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_mouse_buttons")]
    public static partial NativeStatus MouseButtons(
        ViewerHandle viewer, double x, double y, NativeMouseButtons buttons, NativeModifierKeys modifiers,
        out int redraw);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_mouse_move")]
    public static partial NativeStatus MouseMove(
        ViewerHandle viewer, double x, double y, NativeMouseButtons buttons, NativeModifierKeys modifiers,
        out int redraw);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_mouse_leave")]
    public static partial NativeStatus MouseLeave(ViewerHandle viewer, out int redraw);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_scroll")]
    public static partial NativeStatus Scroll(ViewerHandle viewer, double x, double y, double delta, out int redraw);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_redraw")]
    public static partial NativeStatus Redraw(ViewerHandle viewer, out int animating);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_get_diagnostics")]
    public static partial NativeStatus GetDiagnostics(ViewerHandle viewer, out nint text);

    /// <summary>Copies a UTF-8 string owned by the viewer.</summary>
    public static string ToManagedString(nint utf8) => Marshal.PtrToStringUTF8(utf8) ?? string.Empty;

    // The viewer owns the strings it returns: they are declared as pointers and copied, because a string return type
    // would make the marshaller free them.
    public static string LastErrorMessage() => ToManagedString(LastErrorMessagePointer());

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_last_error_message")]
    private static partial nint LastErrorMessagePointer();
}
