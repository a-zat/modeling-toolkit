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
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport.Interop;
using ModelingToolkit.Core.Kernel;
using BlockReference = ModelingToolkit.Core.Model.BlockReference;
using BoundingBox = ModelingToolkit.Core.Geometry.BoundingBox;
using BrepEntity = ModelingToolkit.Core.Model.BrepEntity;
using Transform = ModelingToolkit.Core.Geometry.Transform;
using Document = ModelingToolkit.Core.Model.Document;
using DocumentChange = ModelingToolkit.Core.Model.DocumentChange;
using ElementReference = ModelingToolkit.Core.Model.ElementReference;
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

    public static readonly StyledProperty<ICommand?> InputCommandProperty =
        AvaloniaProperty.Register<ViewportView, ICommand?>(nameof(InputCommand));

    public static readonly StyledProperty<IReadOnlyList<ElementReference>?> HighlightedElementsProperty =
        AvaloniaProperty.Register<ViewportView, IReadOnlyList<ElementReference>?>(nameof(HighlightedElements));

    public static readonly StyledProperty<ICommand?> WarningsCommandProperty =
        AvaloniaProperty.Register<ViewportView, ICommand?>(nameof(WarningsCommand));

    public static readonly StyledProperty<DisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<ViewportView, DisplayMode>(nameof(DisplayMode), DisplayMode.ShadedWithEdges);

    public static readonly StyledProperty<RenderStyle> RenderStyleProperty =
        AvaloniaProperty.Register<ViewportView, RenderStyle>(nameof(RenderStyle));

    public static readonly StyledProperty<bool> IsPerspectiveProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsPerspective), defaultValue: true);

    public static readonly StyledProperty<bool> IsGridVisibleProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsGridVisible), defaultValue: true);

    /// <summary>Where the camera is, as of the last frame drawn; the view sets it, for what is shown by the side it faces.</summary>
    public static readonly StyledProperty<Point3d?> EyeProperty =
        AvaloniaProperty.Register<ViewportView, Point3d?>(nameof(Eye), defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);

    public static readonly StyledProperty<bool> HighlightsHoverProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(HighlightsHover));

    /// <summary>The block references placed otherwise than by their transformation, as by animations.</summary>
    public static readonly StyledProperty<IReadOnlyDictionary<BlockReference, Transform>?> PoseProperty =
        AvaloniaProperty.Register<ViewportView, IReadOnlyDictionary<BlockReference, Transform>?>(nameof(Pose));

    /// <summary>
    /// Whether the camera walks through the model at the height of the eyes (<see cref="WalkCamera"/>): the keys W, A,
    /// S, D and the arrows walk, Shift runs, a drag looks around, the wheel steps, and a click raises a
    /// <see cref="ViewportInputKind.Click"/> on what is under the pointer, without orbiting the view.
    /// </summary>
    public static readonly StyledProperty<bool> IsWalkingProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsWalking));

    /// <summary>Whether walking stops at the parts of the model in the way, sliding along them.</summary>
    public static readonly StyledProperty<bool> CollidesProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(Collides));

    /// <summary>How the person walks in the walk mode.</summary>
    public static readonly StyledProperty<WalkSettings> WalkSettingsProperty =
        AvaloniaProperty.Register<ViewportView, WalkSettings>(nameof(WalkSettings), WalkSettings.Default);

    /// <summary>The widths, in points, of the edges drawn over the faces.</summary>
    public static readonly StyledProperty<double> EdgeWidthProperty =
        AvaloniaProperty.Register<ViewportView, double>(nameof(EdgeWidth), 1.0);

    /// <summary>The widths, in points, of the outlines of the CAD render style.</summary>
    public static readonly StyledProperty<double> OutlineWidthProperty =
        AvaloniaProperty.Register<ViewportView, double>(nameof(OutlineWidth), 1.5);

    /// <summary>The sky and the sun of the rendered display mode.</summary>
    public static readonly StyledProperty<ViewEnvironment> EnvironmentProperty =
        AvaloniaProperty.Register<ViewportView, ViewEnvironment>(nameof(Environment), ViewEnvironment.Noon);

    /// <summary>
    /// Points of the model that a press of the left button takes, to drag them in place of turning the view, such as
    /// the handles of the selection: the press, the drags and the release carry the index of the one pressed.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<Point3d>> HandlesProperty =
        AvaloniaProperty.Register<ViewportView, IReadOnlyList<Point3d>>(nameof(Handles), []);

    public IReadOnlyList<Point3d> Handles
    {
        get => GetValue(HandlesProperty);
        set => SetValue(HandlesProperty, value);
    }

    // How close to a handle a press takes it, in points.
    private const double HandleReach = 9;

    private int? _grabbed; // the handle being dragged
    private Point? _rightFrom; // where the right button was pressed, until it drags

    /// <summary>
    /// Raised by a click of the right button, a press and a release without a drag (which pans): with what is under the
    /// pointer, for a menu of what can be done with it.
    /// </summary>
    public event EventHandler<ViewportPick>? MenuRequested;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // The handle under a point of the view, the closest within reach; null for none.
    private int? HandleAt(Point at)
    {
        if (Handles is not { Count: > 0 } handles || Viewer is not { } viewer)
        {
            return null;
        }
        (double X, double Y)[] projected;
        try
        {
            projected = viewer.Project([.. handles]);
        }
        catch (ViewerException)
        {
            return null;
        }
        int? best = null;
        double bestDistance = HandleReach;
        for (int i = 0; i < projected.Length; i++)
        {
            double distance = Math.Sqrt(Math.Pow(projected[i].X - at.X, 2) + Math.Pow(projected[i].Y - at.Y, 2));
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    public static readonly StyledProperty<ViewOverlay> OverlayProperty =
        AvaloniaProperty.Register<ViewportView, ViewOverlay>(nameof(Overlay), ViewOverlay.Empty);

    /// <summary>
    /// Whether a drag with the Command key (Control elsewhere) selects in a window, in place of turning the view, as
    /// with the select tool.
    /// </summary>
    public static readonly StyledProperty<bool> IsWindowSelectionEnabledProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(IsWindowSelectionEnabled));

    /// <summary>The references to the block being edited, whose shapes a window selects from; empty for the model.</summary>
    public static readonly StyledProperty<IReadOnlyList<Entity>> EditPathProperty =
        AvaloniaProperty.Register<ViewportView, IReadOnlyList<Entity>>(nameof(EditPath), []);

    /// <summary>Receives the <see cref="WindowSelection"/> made by a window.</summary>
    public static readonly StyledProperty<ICommand?> WindowSelectCommandProperty =
        AvaloniaProperty.Register<ViewportView, ICommand?>(nameof(WindowSelectCommand));

    // A pinch that doubles the size of the view zooms as much as about 13 notches of the wheel.
    private const double PinchToWheelNotches = 200.0 / 15.0;

    private readonly NativeViewHost _host;
    private readonly TextBlock _failure;
    private bool _frameRequested;
    private int _shownDocuments;
    private ViewportScene? _shownScene;
    // For the instances shown, and the radius and the height of the eyes it was made for.
    private (IReadOnlyList<ViewportInstance> Instances, WalkSettings Settings, WalkCollider Collider)? _collider;
    private Document? _followedDocument;
    private KeyModifiers _clickModifiers; // held when a button was last released, which ends a click
    private int _clickCount = 1; // of the button last pressed, 2 for a double click
    private ViewportPick _hovered = new(null, null, default, default, KeyModifiers.None); // as of the last frame
    private bool _hoverMoved; // since the last frame, without buttons pressed
    private KeyModifiers _hoverModifiers;
    private bool _updatePosted;
    private Point? _windowStart; // where a window selection began, while it is dragged

    // The walk: the camera, the keys held, the last position of a drag that looks around, and whether it moved.
    private WalkCamera? _walk;
    private ViewCamera? _orbitCamera; // as it was before walking
    private readonly HashSet<Key> _walkKeys = [];
    private bool _running;
    private Point? _lookFrom;
    private double _lookDistance;
    private bool _walkFrameRequested;
    private readonly System.Diagnostics.Stopwatch _walkClock = new();

    public ViewportView()
    {
        // Shown until the native view is drawn, and in the tests, which have no native views. A background also makes
        // this control the target of the pointer events over the native view.
        Bind(BackgroundProperty, this.GetResourceObservable("Mtk.ViewportBackground"));
        ClipToBounds = true;
        Focusable = true; // takes the keys back from the fields, as the measurement field gives them

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
        LostFocus += (_, _) => _walkKeys.Clear(); // the release of the keys held would not come
    }

    public ViewEnvironment Environment
    {
        get => GetValue(EnvironmentProperty);
        set => SetValue(EnvironmentProperty, value);
    }

    public WalkSettings WalkSettings
    {
        get => GetValue(WalkSettingsProperty);
        set => SetValue(WalkSettingsProperty, value);
    }

    public double EdgeWidth
    {
        get => GetValue(EdgeWidthProperty);
        set => SetValue(EdgeWidthProperty, value);
    }

    public double OutlineWidth
    {
        get => GetValue(OutlineWidthProperty);
        set => SetValue(OutlineWidthProperty, value);
    }

    public bool Collides
    {
        get => GetValue(CollidesProperty);
        set => SetValue(CollidesProperty, value);
    }

    public bool IsWalking
    {
        get => GetValue(IsWalkingProperty);
        set => SetValue(IsWalkingProperty, value);
    }

    public IReadOnlyDictionary<BlockReference, Transform>? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    /// <summary>What the active tool draws over the scene, such as the line it draws.</summary>
    public ViewOverlay Overlay
    {
        get => GetValue(OverlayProperty);
        set => SetValue(OverlayProperty, value);
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
    /// Executed with each input of the pointer for the tools (a <see cref="ViewportPick"/>): what is under the pointer
    /// as the view picks it, the path of the mesh or shape, a leaf of the model tree, the element of a shape and the
    /// point, with the modifier keys. Hovers come once per frame at most; presses, drags and releases are those of the
    /// left button, which turns the view too; a click follows the release that ends it. Clicks on the ViewCube turn the
    /// view instead.
    /// </summary>
    public ICommand? InputCommand
    {
        get => GetValue(InputCommandProperty);
        set => SetValue(InputCommandProperty, value);
    }

    public bool IsWindowSelectionEnabled
    {
        get => GetValue(IsWindowSelectionEnabledProperty);
        set => SetValue(IsWindowSelectionEnabledProperty, value);
    }

    public IReadOnlyList<Entity> EditPath
    {
        get => GetValue(EditPathProperty);
        set => SetValue(EditPathProperty, value);
    }

    public ICommand? WindowSelectCommand
    {
        get => GetValue(WindowSelectCommandProperty);
        set => SetValue(WindowSelectCommandProperty, value);
    }

    private static KeyModifiers WindowModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>The faces, edges and vertices of shapes shown as selected; null for none.</summary>
    public IReadOnlyList<ElementReference>? HighlightedElements
    {
        get => GetValue(HighlightedElementsProperty);
        set => SetValue(HighlightedElementsProperty, value);
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

    public RenderStyle RenderStyle
    {
        get => GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
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

    public Point3d? Eye
    {
        get => GetValue(EyeProperty);
        set => SetValue(EyeProperty, value);
    }

    /// <summary>Whether what is under the pointer is tinted; it is found either way.</summary>
    public bool HighlightsHover
    {
        get => GetValue(HighlightsHoverProperty);
        set => SetValue(HighlightsHoverProperty, value);
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
               Find("SystemAccentColor", out Color highlight) && Find("Mtk.Viewport.Edge", out Color edges) &&
               Find("Mtk.Viewport.Outline", out Color outlines)
            ? new ViewportColors(gradient.GradientStops[0].Color, gradient.GradientStops[^1].Color, grid, axisX,
                axisY, axisZ, cubeFace, cubeEdge, cubeText, highlight, edges, outlines)
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
        else if (change.Property == HighlightedElementsProperty && _shownScene is { } shown)
        {
            Send(viewer =>
            {
                shown.ShowElementsIn(viewer, HighlightedElements);
                return true;
            });
        }
        else if (change.Property == SelectionProperty && _shownScene is { } scene)
        {
            Send(viewer =>
            {
                scene.ShowSelectionIn(viewer, Selection);
                return true;
            });
        }
        else if (change.Property == EnvironmentProperty)
        {
            Send(ApplyEnvironment);
        }
        else if (change.Property == WalkSettingsProperty && _walk is { } walking)
        {
            walking.Settings = WalkSettings;
            PlaceWalkCamera(); // the field of view
        }
        else if (change.Property == EdgeWidthProperty || change.Property == OutlineWidthProperty)
        {
            Send(ApplySettings);
        }
        else if (change.Property == IsWalkingProperty)
        {
            SetWalking(IsWalking);
        }
        else if (change.Property == PoseProperty && _shownScene is not null)
        {
            UpdateScene();
        }
        else if (change.Property == OverlayProperty)
        {
            Send(viewer =>
            {
                viewer.SetOverlay(Overlay);
                return true;
            });
        }
        else if (change.Property == RenderStyleProperty)
        {
            ApplyColors();
            Send(ApplySettings);
        }
        else if (change.Property == DisplayModeProperty || change.Property == IsPerspectiveProperty
                 || change.Property == IsGridVisibleProperty || change.Property == HighlightsHoverProperty)
        {
            Send(ApplySettings);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(); // out of the fields, so that typed values and arrows go to the tools
        if (_walk is not null)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _lookFrom = e.GetPosition(_host);
                _lookDistance = 0;
            }
            return;
        }
        if (IsWindowSelectionEnabled && e.KeyModifiers.HasFlag(WindowModifier) &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _windowStart = e.GetPosition(_host); // the viewer does not turn the view
            return;
        }
        _clickCount = e.ClickCount;
        _rightFrom = e.GetCurrentPoint(this).Properties.IsRightButtonPressed ? e.GetPosition(_host) : null;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && HandleAt(e.GetPosition(_host)) is { } handle)
        {
            // The viewer does not turn the view: the handle is dragged.
            _grabbed = handle;
            e.Pointer.Capture(this);
            Raise(_hovered with { Modifiers = e.KeyModifiers, ClickCount = 1, Kind = ViewportInputKind.Press, Handle = handle });
            return;
        }
        SendButtons(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Raise(_hovered with { Modifiers = e.KeyModifiers, ClickCount = e.ClickCount, Kind = ViewportInputKind.Press });
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_walk is not null)
        {
            // A press and a release without a look around is a click on what is under the pointer.
            if (_lookFrom is not null && _lookDistance < 4 && e.InitialPressMouseButton == MouseButton.Left)
            {
                Raise(_hovered with { Modifiers = e.KeyModifiers, ClickCount = 1, Kind = ViewportInputKind.Click });
            }
            _lookFrom = null;
            return;
        }
        if (_windowStart is { } start)
        {
            _windowStart = null;
            SelectInWindow(start, e.GetPosition(_host), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        if (_grabbed is { } grabbed)
        {
            _grabbed = null;
            e.Pointer.Capture(null);
            Raise(_hovered with { Modifiers = e.KeyModifiers, Kind = ViewportInputKind.Release, Handle = grabbed });
            return;
        }
        _clickModifiers = e.KeyModifiers;
        SendButtons(e);
        if (e.InitialPressMouseButton == MouseButton.Right && _rightFrom is { } from)
        {
            _rightFrom = null;
            Point at = e.GetPosition(_host);
            if (Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) < 4)
            {
                MenuRequested?.Invoke(this, _hovered with { Modifiers = e.KeyModifiers, Kind = ViewportInputKind.Click });
            }
        }
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            Raise(_hovered with { Modifiers = e.KeyModifiers, ClickCount = _clickCount, Kind = ViewportInputKind.Release });
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_walk is { } walk && _lookFrom is { } from)
        {
            Point to = e.GetPosition(_host);
            _lookDistance += Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
            if (_lookDistance >= 4)
            {
                walk.Look(to.X - from.X, from.Y - to.Y);
                _lookFrom = to;
                PlaceWalkCamera();
            }
            return;
        }
        if (_walk is not null || _grabbed is not null)
        {
            // Without buttons, the viewer only finds what is under the pointer; for a handle, the frame drags it.
            Send(viewer => viewer.MouseMove(Position(e), NativeMouseButtons.None, OcctViewer.ToNative(e.KeyModifiers)));
            _hoverMoved = true;
            _hoverModifiers = e.KeyModifiers;
            RequestFrame();
            return;
        }
        if (_windowStart is { } start)
        {
            Point at = e.GetPosition(_host);
            Send(viewer =>
            {
                viewer.SetWindowBand(WindowArea.Between(start, at), at.X < start.X);
                return true;
            });
            return;
        }
        Send(viewer => viewer.MouseMove(Position(e), Buttons(e), OcctViewer.ToNative(e.KeyModifiers)));
        PointerPointProperties buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsLeftButtonPressed)
        {
            Raise(_hovered with { Modifiers = e.KeyModifiers, Kind = ViewportInputKind.Drag });
        }
        else if (!buttons.IsRightButtonPressed && !buttons.IsMiddleButtonPressed)
        {
            Cursor = HandleAt(e.GetPosition(_host)) is null ? null : HandCursor;
            // What is under the pointer is known once the next frame has detected it.
            _hoverMoved = true;
            _hoverModifiers = e.KeyModifiers;
            RequestFrame();
        }
    }

    // From left to right, what the window holds whole; from right to left, what it touches too.
    private void SelectInWindow(Point start, Point end, bool add)
    {
        Send(viewer =>
        {
            viewer.SetWindowBand(null, false);
            if (_shownScene is { } scene && WindowSelectCommand is { } command &&
                (Math.Abs(end.X - start.X) > 2 || Math.Abs(end.Y - start.Y) > 2))
            {
                var selection = new WindowSelection(
                    scene.ElementsIn(EditPath, viewer.Project, WindowArea.Between(start, end), end.X < start.X), add);
                if (command.CanExecute(selection))
                {
                    command.Execute(selection);
                }
            }
            return true;
        });
    }

    private void Raise(ViewportPick input)
    {
        if (InputCommand is { } command && command.CanExecute(input))
        {
            command.Execute(input);
        }
    }

    private ViewportPick ToInput(ViewerPick pick, KeyModifiers modifiers, int clickCount, ViewportInputKind kind) =>
        new(_shownScene?.PathOfShown(pick.Instance), _shownScene?.ElementOf(pick), pick.Point, pick.Normal, modifiers,
            clickCount, kind, pick.Ray);

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Send(viewer => viewer.MouseLeave());
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_walk is { } walk)
        {
            walk.Step(e.Delta.Y * 250);
            PlaceWalkCamera();
            e.Handled = true;
            return;
        }
        Send(viewer => viewer.Scroll(Position(e), e.Delta.Y));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _running = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_walk is not null && IsWalkKey(e.Key))
        {
            _walkKeys.Add(e.Key);
            e.Handled = true; // not to the tools, which take the arrows
            RequestWalkFrame();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _running = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_walkKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private static bool IsWalkKey(Key key) =>
        key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right;

    // Entering the walk stands at the middle of what the camera looked at, on the floor of the model; leaving it goes
    // back to the camera as it was.
    private void SetWalking(bool walking)
    {
        Send(viewer =>
        {
            if (walking && _walk is null)
            {
                _orbitCamera = viewer.GetCamera();
                double floor = Document?.GetBoundingBox() is { IsEmpty: false } box ? box.Min.Z : 0;
                _walk = WalkCamera.Enter(_orbitCamera, floor, WalkSettings);
                _walk.Limit = LimitWalk;
                viewer.SetPerspective(true);
                viewer.SetCamera(_walk.ToCamera());
                Focus();
            }
            else if (!walking && _walk is not null)
            {
                _walk = null;
                _walkKeys.Clear();
                _lookFrom = null;
                viewer.SetPerspective(IsPerspective);
                if (_orbitCamera is { } orbit)
                {
                    viewer.SetCamera(orbit);
                }
            }
            return true;
        });
    }

    // Walls stop the walk, as the scene places them now: a door opened by an animation lets it through.
    private Vector3d LimitWalk(Point3d eye, Vector3d move)
    {
        if (!Collides || _shownScene is not { } scene)
        {
            return move;
        }
        WalkSettings settings = WalkSettings;
        if (_collider is not { } collider || !ReferenceEquals(collider.Instances, scene.Instances) ||
            collider.Settings.CollisionRadius != settings.CollisionRadius || collider.Settings.EyeHeight != settings.EyeHeight)
        {
            collider = (scene.Instances, settings,
                new WalkCollider(scene.PlacedMeshes(), settings.CollisionRadius, settings.EyeHeight));
            _collider = collider;
        }
        return collider.Collider.Limit(eye, move);
    }

    private void PlaceWalkCamera()
    {
        if (_walk is { } walk)
        {
            Send(viewer =>
            {
                viewer.SetCamera(walk.ToCamera());
                return true;
            });
        }
    }

    // While walking keys are held, the camera moves on every frame of the window, by the time since the last one.
    private void RequestWalkFrame()
    {
        if (_walkFrameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        if (!_walkClock.IsRunning)
        {
            _walkClock.Restart();
        }
        _walkFrameRequested = true;
        topLevel.RequestAnimationFrame(_ =>
        {
            _walkFrameRequested = false;
            double seconds = Math.Min(_walkClock.Elapsed.TotalSeconds, 0.1); // a stall does not leap
            _walkClock.Restart();
            if (_walk is not { } walk || _walkKeys.Count == 0)
            {
                _walkClock.Reset();
                return;
            }

            double forward = (Held(Key.W) || Held(Key.Up) ? 1 : 0) - (Held(Key.S) || Held(Key.Down) ? 1 : 0);
            double right = (Held(Key.D) || Held(Key.Right) ? 1 : 0) - (Held(Key.A) || Held(Key.Left) ? 1 : 0);
            walk.Walk(forward, right, seconds, _running);
            PlaceWalkCamera();
            _hoverMoved = true; // what is under the pointer has changed
            RequestWalkFrame();
        });

        bool Held(Key key) => _walkKeys.Contains(key);
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
            ViewerPick? picked = null, hovered = null;
            Point3d? eye = null;
            bool hoverMoved = _hoverMoved;
            _hoverMoved = false;
            Send(viewer =>
            {
                bool animating = viewer.Redraw();
                eye = viewer.GetCamera().Eye;
                if (viewer.TakePick(out ViewerPick pick))
                {
                    picked = pick;
                }
                if (hoverMoved)
                {
                    hovered = viewer.GetHovered();
                }

                return animating;
            });
            if (eye is { } seen && seen != Eye)
            {
                Eye = seen;
            }
            if (hovered is { } hover)
            {
                _hovered = ToInput(hover, _hoverModifiers, 1, ViewportInputKind.Hover);
                Raise(_grabbed is { } grabbed ? _hovered with { Kind = ViewportInputKind.Drag, Handle = grabbed } : _hovered);
            }
            if (picked is { } pick)
            {
                Raise(ToInput(pick, _clickModifiers, _clickCount, ViewportInputKind.Click));
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
            viewer.SetOverlay(Overlay); // which clearing the viewer removed

            return true;
        });
        Send(ApplyEnvironment); // the ground under the new model
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

        scene.SetPose(Pose ?? new Dictionary<BlockReference, Transform>());
        Send(viewer =>
        {
            scene.Update();
            scene.ShowSelectionIn(viewer, Selection);
            // The instances changed or removed lost their highlighted elements.
            scene.ShowElementsIn(viewer, HighlightedElements);
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

    // The ground lies under the model, wide enough to reach the horizon from it.
    private bool ApplyEnvironment(OcctViewer viewer)
    {
        BoundingBox box = Document?.GetBoundingBox() ?? BoundingBox.Empty;
        double height = box.IsEmpty ? 0 : box.Min.Z;
        double size = box.IsEmpty ? 100_000 : Math.Max(100_000, 40 * (box.Max - box.Min).Length);
        viewer.SetEnvironment(Environment, height, size);
        return true;
    }

    private bool ApplySettings(OcctViewer viewer)
    {
        ApplyEnvironment(viewer);
        viewer.SetDisplayMode(DisplayMode);
        viewer.SetRenderStyle(RenderStyle);
        viewer.SetPerspective(IsPerspective || _walk is not null);
        viewer.SetGridVisible(IsGridVisible);
        viewer.SetHoverHighlight(HighlightsHover);
        viewer.SetLineWidths(EdgeWidth, OutlineWidth);
        viewer.SetOverlay(Overlay);
        return true;
    }

    // The native view takes its colours from the same resources as the rest of the interface, in the current theme; the
    // CAD render style draws on white, as on paper, whatever the theme.
    private void ApplyColors()
    {
        bool cad = RenderStyle == RenderStyle.Cad;
        if (ReadColors(cad ? ThemeVariant.Light : ActualThemeVariant) is { } themed)
        {
            ViewportColors colors = cad ? themed with { BackgroundTop = Colors.White, BackgroundBottom = Colors.White }
                                        : themed;
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
