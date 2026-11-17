using System.Runtime.InteropServices;

namespace ModelingToolkit.Gui.Viewport.Interop;

/// <summary>P/Invoke declarations of the C API in native/occt-viewer/include/mtk/occt_viewer.h.</summary>
internal static partial class NativeMethods
{
    /// <summary>Must match MTK_OCCT_VIEWER_ABI_VERSION in the native header.</summary>
    public const int AbiVersion = 21;

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

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_add_mesh")]
    public static partial NativeStatus AddMesh(ViewerHandle viewer, in NativeMesh mesh, out int index);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_add_texture")]
    public static unsafe partial NativeStatus AddTexture(
        ViewerHandle viewer, byte* pixels, int width, int height, out int index);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_add_material")]
    public static partial NativeStatus AddMaterial(ViewerHandle viewer, in NativeMaterial material, out int index);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_add_instance")]
    public static unsafe partial NativeStatus AddInstance(
        ViewerHandle viewer, int mesh, int material, double* transform, out int index);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_instance")]
    public static unsafe partial NativeStatus SetInstance(
        ViewerHandle viewer, int instance, int mesh, int material, double* transform);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_remove_instance")]
    public static partial NativeStatus RemoveInstance(ViewerHandle viewer, int instance);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_remove_mesh")]
    public static partial NativeStatus RemoveMesh(ViewerHandle viewer, int mesh);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_selection")]
    public static unsafe partial NativeStatus SetSelection(ViewerHandle viewer, int* instances, int count);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_clear")]
    public static partial NativeStatus Clear(ViewerHandle viewer);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_fit_all")]
    public static partial NativeStatus FitAll(ViewerHandle viewer);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_show_view")]
    public static partial NativeStatus ShowView(ViewerHandle viewer, int view);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_display_mode")]
    public static partial NativeStatus SetDisplayMode(ViewerHandle viewer, int mode);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_render_style")]
    public static partial NativeStatus SetRenderStyle(ViewerHandle viewer, int style);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_perspective")]
    public static partial NativeStatus SetPerspective(ViewerHandle viewer, int perspective);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_get_camera")]
    public static partial NativeStatus GetCamera(ViewerHandle viewer, out NativeCamera camera);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_camera")]
    public static partial NativeStatus SetCamera(ViewerHandle viewer, in NativeCamera camera);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_environment")]
    public static partial NativeStatus SetEnvironment(ViewerHandle viewer, in NativeEnvironment environment);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_line_widths")]
    public static partial NativeStatus SetLineWidths(ViewerHandle viewer, double edgeWidth, double outlineWidth);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_grid_visible")]
    public static partial NativeStatus SetGridVisible(ViewerHandle viewer, int visible);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_hover_highlight")]
    public static partial NativeStatus SetHoverHighlight(ViewerHandle viewer, int highlight);

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

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_take_pick")]
    public static partial NativeStatus TakePick(ViewerHandle viewer, out int picked, out NativePick pick);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_get_hovered")]
    public static partial NativeStatus GetHovered(ViewerHandle viewer, out NativePick pick);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_highlighted_elements")]
    public static unsafe partial NativeStatus SetHighlightedElements(
        ViewerHandle viewer, ViewerElement* elements, int count);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_window_band")]
    public static partial NativeStatus SetWindowBand(
        ViewerHandle viewer, int shown, double x0, double y0, double x1, double y1, int crossing);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_project")]
    public static partial NativeStatus Project(ViewerHandle viewer, ReadOnlySpan<double> points, int count, Span<double> projected);

    [LibraryImport(LibraryName, EntryPoint = "mtk_viewer_set_overlay")]
    public static unsafe partial NativeStatus SetOverlay(ViewerHandle viewer, NativeOverlay* overlay);

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
