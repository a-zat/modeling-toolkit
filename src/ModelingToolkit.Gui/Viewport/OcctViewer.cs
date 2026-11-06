using System.Numerics;
using Avalonia;
using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport.Interop;
using Color = Avalonia.Media.Color;

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
        Copy(colors.Edges, native.Edges);
        NativeMethods.SetColors(_handle, in native).ThrowIfFailed();

        static void Copy(Color color, float* rgb)
        {
            rgb[0] = color.R / 255f;
            rgb[1] = color.G / 255f;
            rgb[2] = color.B / 255f;
        }
    }

    // The scene: each method that adds returns the index of what it added, from 0 until Clear. After changing the scene,
    // Redraw should be called on the next frame.

    /// <param name="edges">
    /// The edges that the display modes with edges draw, such as those of the shape the mesh is the tessellation of;
    /// null for the edges of the triangles.
    /// </param>
    /// <exception cref="ArgumentException">The mesh has no triangles, or positions that are not finite.</exception>
    public unsafe int AddMesh(MeshEntity mesh, Polylines? edges = null)
    {
        // The arrays of the mesh are passed as they are: their elements are made of doubles, floats and ints.
        fixed (Point3d* positions = mesh.Vertices)
        fixed (Vector3* normals = mesh.Normals)
        fixed (Vector2* uvs = mesh.TextureCoordinates)
        fixed (Triangle* triangles = mesh.Triangles)
        fixed (Point3d* edgePoints = edges is null ? default : edges.Points)
        fixed (int* edgeFirstPoints = edges is null ? default : edges.FirstPoints)
        {
            var native = new NativeMesh
            {
                Positions = (double*)positions,
                Normals = (float*)normals,
                Uvs = (float*)uvs,
                VertexCount = mesh.Vertices.Length,
                Triangles = (int*)triangles,
                TriangleCount = mesh.Triangles.Length,
                // Edges of its own, even none, take the place of those of the triangles: the first points of the edges
                // hold at least one value, so their pointer is null only without edges of its own.
                EdgePoints = (double*)edgePoints,
                EdgeFirstPoints = edgeFirstPoints,
                EdgeCount = edges?.Count ?? 0,
            };
            NativeMethods.AddMesh(_handle, in native, out int index).ThrowIfFailed();
            return index;
        }
    }

    public unsafe int AddTexture(TextureImage image)
    {
        fixed (byte* pixels = image.Pixels)
        {
            NativeMethods.AddTexture(_handle, pixels, image.Width, image.Height, out int index).ThrowIfFailed();
            return index;
        }
    }

    /// <param name="texture">The base colour texture, as returned by <see cref="AddTexture"/>, or -1.</param>
    public unsafe int AddMaterial(Material material, int texture)
    {
        // Values out of range, which a file may hold, are brought into it rather than refused.
        var native = new NativeMaterial
        {
            Metallic = ToUnit(material.Metallic),
            Roughness = ToUnit(material.Roughness),
            BaseColorTexture = texture,
        };
        native.BaseColor[0] = ToUnit(material.BaseColor.R);
        native.BaseColor[1] = ToUnit(material.BaseColor.G);
        native.BaseColor[2] = ToUnit(material.BaseColor.B);
        native.BaseColor[3] = ToUnit(material.BaseColor.A);
        NativeMethods.AddMaterial(_handle, in native, out int index).ThrowIfFailed();
        return index;

        static float ToUnit(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
    }

    /// <exception cref="ArgumentException">The placement is not invertible.</exception>
    public unsafe int AddInstance(int mesh, int material, Transform placement)
    {
        double* values = stackalloc double[]
        {
            placement.M00, placement.M01, placement.M02, placement.M03,
            placement.M10, placement.M11, placement.M12, placement.M13,
            placement.M20, placement.M21, placement.M22, placement.M23,
        };
        NativeMethods.AddInstance(_handle, mesh, material, values, out int index).ThrowIfFailed();
        return index;
    }

    /// <summary>
    /// Shows instances as selected, in place of the previous selection: the others are faded and drawn behind them.
    /// With none selected, or all, none is faded.
    /// </summary>
    public unsafe void SetSelection(ReadOnlySpan<int> instances)
    {
        fixed (int* values = instances)
        {
            NativeMethods.SetSelection(_handle, values, instances.Length).ThrowIfFailed();
        }
    }

    /// <summary>Removes the meshes, textures, materials and instances.</summary>
    public void Clear() => NativeMethods.Clear(_handle).ThrowIfFailed();

    /// <summary>Turns the camera to show all the instances; with none, the view does not change.</summary>
    public void FitAll() => NativeMethods.FitAll(_handle).ThrowIfFailed();

    /// <summary>Turns the camera to a standard view, fitting the instances, with an animation.</summary>
    public void ShowView(StandardView view) => NativeMethods.ShowView(_handle, (int)view).ThrowIfFailed();

    public void SetDisplayMode(DisplayMode mode) => NativeMethods.SetDisplayMode(_handle, (int)mode).ThrowIfFailed();

    public void SetPerspective(bool perspective) =>
        NativeMethods.SetPerspective(_handle, perspective ? 1 : 0).ThrowIfFailed();

    /// <summary>Shows or hides the grid on the ground and the axes through the origin.</summary>
    public void SetGridVisible(bool visible) => NativeMethods.SetGridVisible(_handle, visible ? 1 : 0).ThrowIfFailed();

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

    /// <summary>
    /// Whether the view was clicked since the last call, other than on the ViewCube: the clicks are applied by
    /// <see cref="Redraw"/>. The viewer shows no selection by itself: see <see cref="SetSelection"/>.
    /// </summary>
    /// <param name="instance">The instance under the pointer, or -1 for none.</param>
    public bool TakePick(out int instance)
    {
        NativeMethods.TakePick(_handle, out int picked, out instance).ThrowIfFailed();
        return picked != 0;
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
