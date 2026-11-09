using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport.Interop;
using ModelingToolkit.Core.Kernel;
using BrepEntity = ModelingToolkit.Core.Model.BrepEntity;
using Document = ModelingToolkit.Core.Model.Document;
using DocumentChange = ModelingToolkit.Core.Model.DocumentChange;
using Entity = ModelingToolkit.Core.Model.Entity;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The 3D view of the document. OpenCASCADE draws it in a native view, which the operating system shows over the
/// content of the window: nothing drawn by Avalonia can cover it (#25), while popups, menus and tooltips can, as they
/// are windows of their own. The native view lets the mouse through, so the input arrives here as for any other
/// control and is passed on to the viewer, which is drawn again on the next frame of the window.
/// </summary>
public sealed class ViewportView : Border, IViewCamera
{
    public static readonly StyledProperty<Document?> DocumentProperty =
        AvaloniaProperty.Register<ViewportView, Document?>(nameof(Document));

    public static readonly StyledProperty<IReadOnlyList<Entity>?> SelectionProperty =
        AvaloniaProperty.Register<ViewportView, IReadOnlyList<Entity>?>(nameof(Selection));

    public static readonly StyledProperty<ICommand?> PickCommandProperty =
        AvaloniaProperty.Register<ViewportView, ICommand?>(nameof(PickCommand));

    public static readonly StyledProperty<ICommand?> WarningsCommandProperty =
        AvaloniaProperty.Register<ViewportView, ICommand?>(nameof(WarningsCommand));

    public static readonly StyledProperty<DisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<ViewportView, DisplayMode>(nameof(DisplayMode));

    public static readonly StyledProperty<bool> IsPerspectiveProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsPerspective), defaultValue: true);

    public static readonly StyledProperty<bool> IsGridVisibleProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsGridVisible), defaultValue: true);

    // A pinch that doubles the size of the view zooms as much as about 13 notches of the wheel.
    private const double PinchToWheelNotches = 200.0 / 15.0;

    private readonly NativeViewHost _host;
    private readonly TextBlock _failure;
    private bool _frameRequested;
    private int _shownDocuments;
    private ViewportScene? _shownScene;
    private Document? _followedDocument;
    private bool _updatePosted;

    public ViewportView()
    {
        // Shown until the native view is drawn, and in the tests, which have no native views. A background also makes
        // this control the target of the pointer events over the native view.
        Bind(BackgroundProperty, this.GetResourceObservable("Mtk.ViewportBackground"));
        ClipToBounds = true;

        _host = new NativeViewHost(this);
        _failure = new TextBlock
        {
            IsVisible = false,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _failure.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Mtk.ViewportForeground"));
        Child = new Panel { Children = { _host, _failure } };

        PointerTouchPadGestureMagnify += (_, e) =>
        {
            Send(viewer => viewer.Scroll(Position(e), e.Delta.X * PinchToWheelNotches));
            e.Handled = true;
        };
        ActualThemeVariantChanged += (_, _) => ApplyColors();
    }

    /// <summary>The document shown, which replaces the previous one; the camera turns to it.</summary>
    public Document? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// The selected node of the model tree, as its path: the entities from the model to the node, empty for the whole
    /// model. The rest of the document is faded around it. Null for none.
    /// </summary>
    public IReadOnlyList<Entity>? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>
    /// Executed when the view is clicked, with the path of the mesh under the pointer, a leaf of the model tree, or
    /// null when there is none. Clicks on the ViewCube turn the view instead.
    /// </summary>
    public ICommand? PickCommand
    {
        get => GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    /// <summary>
    /// Executed once the scene of a document is ready, with what it leaves out (see <see cref="ViewportScene.Warnings"/>),
    /// when it leaves out anything.
    /// </summary>
    public ICommand? WarningsCommand
    {
        get => GetValue(WarningsCommandProperty);
        set => SetValue(WarningsCommandProperty, value);
    }

    public DisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>Perspective, or orthographic (parallel) projection.</summary>
    public bool IsPerspective
    {
        get => GetValue(IsPerspectiveProperty);
        set => SetValue(IsPerspectiveProperty, value);
    }

    /// <summary>Whether the grid on the ground and the axes through the origin are shown.</summary>
    public bool IsGridVisible
    {
        get => GetValue(IsGridVisibleProperty);
        set => SetValue(IsGridVisibleProperty, value);
    }

    private OcctViewer? Viewer => _host.Viewer;

    public void FitAll() => Send(viewer =>
    {
        viewer.FitAll();
        return true;
    });

    public void ShowView(StandardView view) => Send(viewer =>
    {
        viewer.ShowView(view);
        return true;
    });

    /// <summary>The colours of the 3D view in a variant of the theme, or null if a resource is missing.</summary>
    internal ViewportColors? ReadColors(ThemeVariant theme)
    {
        return this.TryFindResource("Mtk.ViewportBackground", theme, out object? background) &&
               background is LinearGradientBrush { GradientStops.Count: >= 2 } gradient &&
               Find("Mtk.Viewport.Grid", out Color grid) &&
               Find("Mtk.Viewport.AxisX", out Color axisX) && Find("Mtk.Viewport.AxisY", out Color axisY) &&
               Find("Mtk.Viewport.AxisZ", out Color axisZ) && Find("Mtk.ViewCube.Face", out Color cubeFace) &&
               Find("Mtk.ViewCube.Edge", out Color cubeEdge) && Find("Mtk.ViewCube.Text", out Color cubeText) &&
               Find("SystemAccentColor", out Color highlight) && Find("Mtk.Viewport.Edge", out Color edges)
            ? new ViewportColors(gradient.GradientStops[0].Color, gradient.GradientStops[^1].Color, grid, axisX,
                axisY, axisZ, cubeFace, cubeEdge, cubeText, highlight, edges)
            : null;

        bool Find(string key, out Color color)
        {
            bool found = this.TryFindResource(key, theme, out object? value) && value is Color;
            color = found ? (Color)value! : default;
            return found;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            Follow(Document);
            _ = ShowDocumentAsync();
        }
        else if (change.Property == SelectionProperty && _shownScene is { } scene)
        {
            Send(viewer =>
            {
                scene.ShowSelectionIn(viewer, Selection);
                return true;
            });
        }
        else if (change.Property == DisplayModeProperty || change.Property == IsPerspectiveProperty
                 || change.Property == IsGridVisibleProperty)
        {
            Send(ApplySettings);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        SendButtons(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        SendButtons(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Send(viewer => viewer.MouseMove(Position(e), Buttons(e), OcctViewer.ToNative(e.KeyModifiers)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Send(viewer => viewer.MouseLeave());
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Send(viewer => viewer.Scroll(Position(e), e.Delta.Y));
        e.Handled = true;
    }

    private void SendButtons(PointerEventArgs e) =>
        Send(viewer => viewer.MouseButtons(Position(e), Buttons(e), OcctViewer.ToNative(e.KeyModifiers)));

    private Point Position(PointerEventArgs e) => e.GetPosition(_host);

    private NativeMouseButtons Buttons(PointerEventArgs e) => OcctViewer.ToNative(e.GetCurrentPoint(this).Properties);

    // Passes input to the viewer, and asks for a frame when the view changed.
    private void Send(Func<OcctViewer, bool> input)
    {
        if (Viewer is not { } viewer || _failure.IsVisible)
        {
            return;
        }

        try
        {
            if (input(viewer))
            {
                RequestFrame();
            }
        }
        catch (ViewerException exception)
        {
            ShowFailure(exception.Message);
        }
    }

    // One frame per frame of the window, however many events arrive in between, and more while the view animates.
    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _frameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            ViewerPick? picked = null;
            Send(viewer =>
            {
                bool animating = viewer.Redraw();
                if (viewer.TakePick(out ViewerPick pick))
                {
                    picked = pick;
                }

                return animating;
            });
            if (picked is { } pick)
            {
                IReadOnlyList<Entity>? path = _shownScene?.PathOfShown(pick.Instance);
                if (PickCommand is { } command && command.CanExecute(path))
                {
                    command.Execute(path);
                }
            }
        });
    }

    private void OnViewerCreated()
    {
        ApplyColors();
        Send(ApplySettings);
        // The first frame also tells whether the view could be shown in the window.
        RequestFrame();
        _ = ShowDocumentAsync();
    }

    // The scene is prepared away from the user interface, the textures decoded, and handed to the viewer on its thread,
    // unless another document came in the meantime.
    private async Task ShowDocumentAsync()
    {
        if (Viewer is null)
        {
            return; // shown once the viewer exists
        }

        int shown = ++_shownDocuments;
        Document? document = Document;
        (ViewportScene Scene, TextureImage?[] Images)? prepared = null;
        IReadOnlyList<string> warnings = [];
        try
        {
            if (document is not null)
            {
                prepared = await Task.Run(() =>
                {
                    var scene = ViewportScene.Of(document);
                    return (scene, scene.Textures.Select(TextureImage.Decode).ToArray());
                });
                warnings = prepared.Value.Scene.Warnings;
            }
        }
        catch (Exception exception)
        {
            // Nothing awaits this method: a failure would go unnoticed, with the previous document still shown.
            warnings = [$"The 3D view cannot show the document: {exception.Message}"];
        }
        if (shown != _shownDocuments)
        {
            return;
        }

        _shownScene = prepared?.Scene;
        if (warnings.Count > 0 && WarningsCommand is { } command && command.CanExecute(warnings))
        {
            command.Execute(warnings);
        }
        Send(viewer =>
        {
            if (prepared is { } ready)
            {
                ready.Scene.ShowIn(viewer, ready.Images);
            }
            else
            {
                viewer.Clear();
            }

            return true;
        });
        // With the changes made while the scene was prepared, and the selection.
        UpdateScene();
    }

    // The changes of the document shown update the scene; those of the previous document are no longer of interest.
    private void Follow(Document? document)
    {
        if (_followedDocument is not null)
        {
            _followedDocument.Changed -= OnDocumentChanged;
        }
        _followedDocument = document;
        if (document is not null)
        {
            document.Changed += OnDocumentChanged;
        }
    }

    // The changes made together, such as those of one command, update the scene once, after them.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        if (_updatePosted)
        {
            return;
        }

        _updatePosted = true;
        Dispatcher.UIThread.Post(UpdateScene, DispatcherPriority.Background);
    }

    private void UpdateScene()
    {
        _updatePosted = false;
        if (_shownScene is not { } scene)
        {
            return; // the scene being prepared is updated once ready
        }

        Send(viewer =>
        {
            scene.Update();
            scene.ShowSelectionIn(viewer, Selection);
            return true;
        });
        foreach ((BrepEntity entity, IKernelShape shape) in scene.TakeShapesToTessellate())
        {
            _ = TessellateAsync(scene, entity, shape);
        }
    }

    // A shape that changed is tessellated away from the user interface; the scene discards the tessellation if the shape
    // has changed again meanwhile, or if another document is shown.
    private async Task TessellateAsync(ViewportScene scene, BrepEntity entity, IKernelShape shape)
    {
        ShapeMesh result = await Task.Run(() => ViewportScene.Tessellate(entity, shape));
        if (scene != _shownScene || !scene.Accept(result))
        {
            return;
        }

        if (result.Warning is { } warning && WarningsCommand is { } command && command.CanExecute(new[] { warning }))
        {
            command.Execute(new[] { warning });
        }
        UpdateScene();
    }

    private bool ApplySettings(OcctViewer viewer)
    {
        viewer.SetDisplayMode(DisplayMode);
        viewer.SetPerspective(IsPerspective);
        viewer.SetGridVisible(IsGridVisible);
        return true;
    }

    // The native view takes its colours from the same resources as the rest of the interface, in the current theme.
    private void ApplyColors()
    {
        if (ReadColors(ActualThemeVariant) is { } colors)
        {
            Send(viewer =>
            {
                viewer.SetColors(colors);
                return true;
            });
        }
    }

    private void ShowFailure(string message)
    {
        // The native view would cover the message.
        _host.IsVisible = false;
        _failure.Text = $"The 3D view is not available.\n{message}";
        _failure.IsVisible = true;
        Logger.TryGet(LogEventLevel.Error, "Viewport")?.Log(this, "The 3D view is not available: {Message}", message);
    }

    /// <summary>Creates the viewer with its native view, and destroys both with the control.</summary>
    private sealed class NativeViewHost(ViewportView owner) : NativeControlHost
    {
        public OcctViewer? Viewer { get; private set; }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            try
            {
                Viewer = OcctViewer.Create(parent.Handle);
            }
            catch (Exception exception)
                when (exception is ViewerException or DllNotFoundException or EntryPointNotFoundException)
            {
                // After Avalonia has attached the empty native view returned here, which ShowFailure hides.
                Dispatcher.UIThread.Post(() => owner.ShowFailure(exception.Message));
                return base.CreateNativeControlCore(parent);
            }

            owner.OnViewerCreated();
            return new PlatformHandle(Viewer.NativeView, OcctViewer.NativeViewDescriptor);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (Viewer is null)
            {
                base.DestroyNativeControlCore(control);
                return;
            }

            Viewer.Dispose();
            Viewer = null;
        }
    }
}
