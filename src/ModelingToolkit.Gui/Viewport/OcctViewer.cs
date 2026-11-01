using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The OpenCASCADE viewer of the native library (native/occt-viewer) and the native view it draws into. Every method
/// must be called on the thread of the user interface. Positions are in device-independent units from the top-left
/// corner of the view, as Avalonia gives them.
/// </summary>
internal sealed class OcctViewer : IDisposable
{
    private readonly ViewerHandle _handle;

    private OcctViewer(ViewerHandle handle, nint nativeView)
    {
        _handle = handle;
        NativeView = nativeView;
    }

    /// <summary>The native view: an NSView on macOS, a child window (HWND) on Windows.</summary>
    public nint NativeView { get; }

    /// <summary>The kind of <see cref="NativeView"/>, as Avalonia names it in a platform handle.</summary>
    public static string NativeViewDescriptor => OperatingSystem.IsWindows() ? "HWND" : "NSView";

    /// <summary>Creates a viewer and its native view.</summary>
    /// <param name="parent">The window that will contain the view, on Windows; ignored on macOS.</param>
    public static OcctViewer Create(nint parent)
    {
        int nativeAbiVersion = NativeMethods.ViewerAbiVersion();
        if (nativeAbiVersion != NativeMethods.AbiVersion)
        {
            throw new ViewerException(
                $"The native viewer implements ABI version {nativeAbiVersion}, but version {NativeMethods.AbiVersion} " +
                "is expected: rebuild the solution.");
        }

        NativeMethods.Create(parent, out ViewerHandle handle).ThrowIfFailed();
        try
        {
            NativeMethods.GetNativeView(handle, out nint view).ThrowIfFailed();
            return new OcctViewer(handle, view);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Converts the buttons pressed into the flags of the viewer.</summary>
    public static NativeMouseButtons ToNative(PointerPointProperties properties) =>
        (properties.IsLeftButtonPressed ? NativeMouseButtons.Left : NativeMouseButtons.None) |
        (properties.IsMiddleButtonPressed ? NativeMouseButtons.Middle : NativeMouseButtons.None) |
        (properties.IsRightButtonPressed ? NativeMouseButtons.Right : NativeMouseButtons.None);

    /// <summary>Converts the modifier keys held into the flags of the viewer.</summary>
    public static NativeModifierKeys ToNative(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? NativeModifierKeys.Shift : NativeModifierKeys.None) |
        (modifiers.HasFlag(KeyModifiers.Control) ? NativeModifierKeys.Control : NativeModifierKeys.None) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? NativeModifierKeys.Alt : NativeModifierKeys.None) |
        (modifiers.HasFlag(KeyModifiers.Meta) ? NativeModifierKeys.Meta : NativeModifierKeys.None);

    public unsafe void SetColors(ViewportColors colors)
    {
        var native = new ViewerColors();
        Copy(colors.BackgroundTop, native.BackgroundTop);
        Copy(colors.BackgroundBottom, native.BackgroundBottom);
        Copy(colors.Grid, native.Grid);
        Copy(colors.AxisX, native.AxisX);
        Copy(colors.AxisY, native.AxisY);
        Copy(colors.AxisZ, native.AxisZ);
        Copy(colors.CubeFace, native.CubeFace);
        Copy(colors.CubeEdge, native.CubeEdge);
        Copy(colors.CubeText, native.CubeText);
        Copy(colors.Highlight, native.Highlight);
        NativeMethods.SetColors(_handle, in native).ThrowIfFailed();

        static void Copy(Color color, float* rgb)
        {
            rgb[0] = color.R / 255f;
            rgb[1] = color.G / 255f;
            rgb[2] = color.B / 255f;
        }
    }

    // Each input method returns whether the view changed: Redraw should then be called on the next frame.

    public bool MouseButtons(Point position, NativeMouseButtons buttons, NativeModifierKeys modifiers)
    {
        NativeMethods.MouseButtons(_handle, position.X, position.Y, buttons, modifiers, out int redraw).ThrowIfFailed();
        return redraw != 0;
    }

    public bool MouseMove(Point position, NativeMouseButtons buttons, NativeModifierKeys modifiers)
    {
        NativeMethods.MouseMove(_handle, position.X, position.Y, buttons, modifiers, out int redraw).ThrowIfFailed();
        return redraw != 0;
    }

    public bool MouseLeave()
    {
        NativeMethods.MouseLeave(_handle, out int redraw).ThrowIfFailed();
        return redraw != 0;
    }

    /// <param name="delta">Notches of the mouse wheel, positive to zoom in.</param>
    public bool Scroll(Point position, double delta)
    {
        NativeMethods.Scroll(_handle, position.X, position.Y, delta, out int redraw).ThrowIfFailed();
        return redraw != 0;
    }

    /// <summary>Applies the input received since the last call and draws the view if it changed.</summary>
    /// <returns>Whether an animation, of the camera for example, needs more frames.</returns>
    /// <exception cref="ViewerException">The view could not be shown in its window.</exception>
    public bool Redraw()
    {
        NativeMethods.Redraw(_handle, out int animating).ThrowIfFailed();
        return animating != 0;
    }

    /// <summary>Description of the view and of the OpenGL driver, one "name: value" per line.</summary>
    public string Diagnostics()
    {
        NativeMethods.GetDiagnostics(_handle, out nint text).ThrowIfFailed();
        return NativeMethods.ToManagedString(text);
    }

    public void Dispose() => _handle.Dispose();
}
