using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport.Interop;
using Color = Avalonia.Media.Color;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// What a click of the view picks: an instance, -1 for none, and the element of its shape under the pointer, with the
/// point picked and, on a face, its normal.
/// </summary>
internal readonly record struct ViewerPick(
    int Instance, NativeElementKind Kind, int Index, Point3d Point, Vector3 Normal, PointerRay? Ray = null);

/// <summary>
/// The OpenCASCADE viewer of the native library (native/occt-viewer) and the native view it draws into. Every method
/// must be called on the thread of the user interface. Positions are in device-independent units from the top-left
/// corner of the view, as Avalonia gives them.
/// </summary>
internal sealed class OcctViewer : IViewerScene, IDisposable
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
        Copy(colors.Outlines, native.Outlines);
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
    /// <param name="shape">
    /// The tessellation of the shape the mesh is, whose faces, edges and vertices are then picked one by one; null for a
    /// mesh picked as a whole.
    /// </param>
    /// <exception cref="ArgumentException">The mesh has no triangles, or positions that are not finite.</exception>
    public unsafe int AddMesh(
        MeshEntity mesh, Polylines? edges = null, ShapeTessellation? shape = null, int[]? faceMaterials = null)
    {
        if (faceMaterials is not null && (shape is null || faceMaterials.Length != shape.FaceCount))
        {
            throw new ArgumentException("The materials of the faces need one per face of the shape.", nameof(faceMaterials));
        }
        int[]? faces = shape is null
            ? null
            : [.. Enumerable.Range(0, shape.FaceCount).Select(face => shape.TrianglesOf(face).Start.Value),
                mesh.Triangles.Length];
        // The arrays of the mesh are passed as they are: their elements are made of doubles, floats and ints.
        fixed (Point3d* positions = mesh.Vertices)
        fixed (Vector3* normals = mesh.Normals)
        fixed (Vector2* uvs = mesh.TextureCoordinates)
        fixed (Triangle* triangles = mesh.Triangles)
        fixed (Point3d* edgePoints = edges is null ? default : edges.Points)
        fixed (int* edgeFirstPoints = edges is null ? default : edges.FirstPoints)
        fixed (int* faceFirstTriangles = faces)
        fixed (int* edgeIndices = shape is null ? default : shape.EdgeIndices)
        fixed (Point3d* shapeVertices = shape is null ? default : shape.Vertices)
        fixed (int* materials = faceMaterials)
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
                FaceFirstTriangles = faceFirstTriangles,
                FaceCount = shape?.FaceCount ?? 0,
                EdgeIndices = edgeIndices,
                ShapeVertices = (double*)shapeVertices,
                ShapeVertexCount = shape?.Vertices.Length ?? 0,
                FaceMaterials = materials,
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
        double* values = stackalloc double[12];
        Write(placement, values);
        NativeMethods.AddInstance(_handle, mesh, material, values, out int index).ThrowIfFailed();
        return index;
    }

    /// <summary>
    /// Changes the mesh, the material and the placement of an instance, keeping its index and whether it is faded.
    /// </summary>
    /// <exception cref="ArgumentException">The instance was removed, or the placement is not invertible.</exception>
    public unsafe void SetInstance(int instance, int mesh, int material, Transform placement)
    {
        double* values = stackalloc double[12];
        Write(placement, values);
        NativeMethods.SetInstance(_handle, instance, mesh, material, values).ThrowIfFailed();
    }

    /// <summary>
    /// Removes an instance; the others keep their indices, and its index is not given again. The selection stays as
    /// it is.
    /// </summary>
    public void RemoveInstance(int instance) => NativeMethods.RemoveInstance(_handle, instance).ThrowIfFailed();

    /// <summary>Releases a mesh that no instance places any more.</summary>
    /// <exception cref="ArgumentException">An instance still places the mesh.</exception>
    public void RemoveMesh(int mesh) => NativeMethods.RemoveMesh(_handle, mesh).ThrowIfFailed();

    // The first three rows of the matrix, as the viewer takes them.
    private static unsafe void Write(Transform placement, double* values)
    {
        values[0] = placement.M00;
        values[1] = placement.M01;
        values[2] = placement.M02;
        values[3] = placement.M03;
        values[4] = placement.M10;
        values[5] = placement.M11;
        values[6] = placement.M12;
        values[7] = placement.M13;
        values[8] = placement.M20;
        values[9] = placement.M21;
        values[10] = placement.M22;
        values[11] = placement.M23;
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

    public void SetRenderStyle(RenderStyle style) => NativeMethods.SetRenderStyle(_handle, (int)style).ThrowIfFailed();

    public void SetPerspective(bool perspective) =>
        NativeMethods.SetPerspective(_handle, perspective ? 1 : 0).ThrowIfFailed();

    /// <summary>Shows or hides the grid on the ground and the axes through the origin.</summary>
    public unsafe ViewCamera GetCamera()
    {
        NativeMethods.GetCamera(_handle, out NativeCamera camera).ThrowIfFailed();
        return new ViewCamera(
            new Point3d(camera.Eye[0], camera.Eye[1], camera.Eye[2]),
            new Point3d(camera.Target[0], camera.Target[1], camera.Target[2]),
            new Vector3d(camera.Up[0], camera.Up[1], camera.Up[2]),
            camera.FieldOfView);
    }

    /// <summary>Places the camera at once, without animation; the view is drawn again on the next frame.</summary>
    public unsafe void SetCamera(ViewCamera camera)
    {
        var native = new NativeCamera { FieldOfView = camera.FieldOfView };
        native.Eye[0] = camera.Eye.X;
        native.Eye[1] = camera.Eye.Y;
        native.Eye[2] = camera.Eye.Z;
        native.Target[0] = camera.Target.X;
        native.Target[1] = camera.Target.Y;
        native.Target[2] = camera.Target.Z;
        native.Up[0] = camera.Up.X;
        native.Up[1] = camera.Up.Y;
        native.Up[2] = camera.Up.Z;
        NativeMethods.SetCamera(_handle, native).ThrowIfFailed();
    }

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
    /// <param name="pick">What is under the pointer: an instance of -1 for nothing.</param>
    public bool TakePick(out ViewerPick pick)
    {
        NativeMethods.TakePick(_handle, out int picked, out NativePick native).ThrowIfFailed();
        pick = ToPick(native);
        return picked != 0;
    }

    /// <summary>What is under the pointer, as the last <see cref="Redraw"/> detected it; instance -1 for nothing.</summary>
    public ViewerPick GetHovered()
    {
        NativeMethods.GetHovered(_handle, out NativePick native).ThrowIfFailed();
        return ToPick(native);
    }

    private static unsafe ViewerPick ToPick(NativePick native)
    {
        var direction = new Vector3d(native.RayDirection[0], native.RayDirection[1], native.RayDirection[2]);
        PointerRay? ray = direction == default ? null : new PointerRay(
            new Point3d(native.RayOrigin[0], native.RayOrigin[1], native.RayOrigin[2]), direction,
            native.PixelSize[0], native.PixelSize[1]);
        return new ViewerPick(
            native.Instance, native.Kind, native.Index, new Point3d(native.Point[0], native.Point[1], native.Point[2]),
            new Vector3(native.Normal[0], native.Normal[1], native.Normal[2]), ray);
    }

    /// <summary>
    /// Highlights elements of the shapes of instances as selected, in place of those highlighted before. An element
    /// whose instance is changed or removed is no longer highlighted.
    /// </summary>
    /// <exception cref="ArgumentException">An element is not one that an instance shows.</exception>
    public unsafe void SetHighlightedElements(ReadOnlySpan<ViewerElement> elements)
    {
        fixed (ViewerElement* values = elements)
        {
            NativeMethods.SetHighlightedElements(_handle, values, elements.Length).ThrowIfFailed();
        }
    }

    /// <summary>Shows an overlay in place of the previous one; an empty one shows none.</summary>
    /// <exception cref="ArgumentException">The viewer refuses the overlay: points that are not finite, for example.</exception>
    /// <summary>Shows the rectangle of a window selection between two corners, dashed when crossing; null hides it.</summary>
    public void SetWindowBand(WindowArea? window, bool crossing)
    {
        WindowArea area = window ?? default;
        NativeMethods.SetWindowBand(_handle, window is null ? 0 : 1, area.Left, area.Top, area.Right, area.Bottom,
            crossing ? 1 : 0).ThrowIfFailed();
    }

    /// <summary>Where points of the model space are in the view, in device-independent units; NaN behind the camera.</summary>
    public (double X, double Y)[] Project(Point3d[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        double[] values = new double[points.Length * 3];
        for (int i = 0; i < points.Length; i++)
        {
            (values[3 * i], values[3 * i + 1], values[3 * i + 2]) = (points[i].X, points[i].Y, points[i].Z);
        }
        double[] projected = new double[points.Length * 2];
        NativeMethods.Project(_handle, values, points.Length, projected).ThrowIfFailed();
        return [.. Enumerable.Range(0, points.Length).Select(i => (projected[2 * i], projected[2 * i + 1]))];
    }

    public unsafe void SetOverlay(ViewOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (overlay.IsEmpty)
        {
            NativeMethods.SetOverlay(_handle, null).ThrowIfFailed();
            return;
        }

        // The points and the texts in native memory for the call only.
        var allocations = new List<nint>();
        nint Allocate(int bytes)
        {
            nint memory = (nint)NativeMemory.Alloc((nuint)Math.Max(bytes, 1));
            allocations.Add(memory);
            return memory;
        }

        try
        {
            var lines = (NativeOverlayLine*)Allocate(overlay.Lines.Count * sizeof(NativeOverlayLine));
            for (int i = 0; i < overlay.Lines.Count; i++)
            {
                OverlayLine line = overlay.Lines[i];
                var points = (double*)Allocate(line.Points.Count * 3 * sizeof(double));
                for (int p = 0; p < line.Points.Count; p++)
                {
                    points[3 * p] = line.Points[p].X;
                    points[3 * p + 1] = line.Points[p].Y;
                    points[3 * p + 2] = line.Points[p].Z;
                }
                lines[i] = new NativeOverlayLine
                {
                    Points = points, PointCount = line.Points.Count, Width = (float)line.Width, Dash = (float)line.Dash,
                };
                SetColor(lines[i].Color, line.Color);
            }

            var markers = (NativeOverlayMarker*)Allocate(overlay.Markers.Count * sizeof(NativeOverlayMarker));
            for (int i = 0; i < overlay.Markers.Count; i++)
            {
                OverlayMarker marker = overlay.Markers[i];
                markers[i] = new NativeOverlayMarker { Shape = (int)marker.Shape };
                SetPoint(markers[i].Point, marker.Point);
                SetColor(markers[i].Color, marker.Color);
            }

            var labels = (NativeOverlayLabel*)Allocate(overlay.Labels.Count * sizeof(NativeOverlayLabel));
            for (int i = 0; i < overlay.Labels.Count; i++)
            {
                OverlayLabel label = overlay.Labels[i];
                byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(label.Text);
                var text = (byte*)Allocate(utf8.Length + 1);
                utf8.CopyTo(new Span<byte>(text, utf8.Length));
                text[utf8.Length] = 0;
                labels[i] = new NativeOverlayLabel { Text = text };
                SetPoint(labels[i].Point, label.Point);
                SetColor(labels[i].Color, label.Color);
                SetColor(labels[i].Background, label.Background);
            }

            var native = new NativeOverlay
            {
                Lines = lines, LineCount = overlay.Lines.Count, Markers = markers, MarkerCount = overlay.Markers.Count,
                Labels = labels, LabelCount = overlay.Labels.Count,
            };
            NativeMethods.SetOverlay(_handle, &native).ThrowIfFailed();
        }
        finally
        {
            foreach (nint memory in allocations)
            {
                NativeMemory.Free((void*)memory);
            }
        }

        static void SetPoint(double* values, Point3d point)
        {
            values[0] = point.X;
            values[1] = point.Y;
            values[2] = point.Z;
        }

        static void SetColor(float* values, Avalonia.Media.Color color)
        {
            values[0] = color.R / 255f;
            values[1] = color.G / 255f;
            values[2] = color.B / 255f;
        }
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
