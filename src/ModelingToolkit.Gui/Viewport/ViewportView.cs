using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The 3D view of the document. OpenCASCADE draws it in a native view, which the operating system shows over the
/// content of the window: nothing drawn by Avalonia can cover it (#25), while popups, menus and tooltips can, as they
/// are windows of their own. The native view lets the mouse through, so the input arrives here as for any other
/// control and is passed on to the viewer, which is drawn again on the next frame of the window.
/// </summary>
public sealed class ViewportView : Border
{
    public static readonly StyledProperty<ViewCubeStyle> CubeStyleProperty =
        AvaloniaProperty.Register<ViewportView, ViewCubeStyle>(nameof(CubeStyle));

    // A pinch that doubles the size of the view zooms as much as about 13 notches of the wheel.
    private const double PinchToWheelNotches = 200.0 / 15.0;

    private readonly NativeViewHost _host;
    private readonly TextBlock _failure;
    private bool _frameRequested;

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

    /// <summary>Prototype (#12): the style of the ViewCube, while one is chosen.</summary>
    public ViewCubeStyle CubeStyle
    {
        get => GetValue(CubeStyleProperty);
        set => SetValue(CubeStyleProperty, value);
    }

    private OcctViewer? Viewer => _host.Viewer;

    /// <summary>The colours of the 3D view in a variant of the theme, or null if a resource is missing.</summary>
    internal ViewportColors? ReadColors(ThemeVariant theme)
    {
        string cube = $"Mtk.ViewCube.{CubeStyle}.";
        return this.TryFindResource("Mtk.ViewportBackground", theme, out object? background) &&
               background is LinearGradientBrush { GradientStops.Count: >= 2 } gradient &&
               Find("Mtk.Viewport.AxisX", out Color axisX) && Find("Mtk.Viewport.AxisY", out Color axisY) &&
               Find("Mtk.Viewport.AxisZ", out Color axisZ) && Find(cube + "Face", out Color cubeFace) &&
               Find(cube + "Edge", out Color cubeEdge) && Find(cube + "Text", out Color cubeText) &&
               Find("SystemAccentColor", out Color highlight)
            ? new ViewportColors(gradient.GradientStops[0].Color, gradient.GradientStops[^1].Color, axisX, axisY,
                axisZ, cubeFace, cubeEdge, cubeText, highlight)
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
        if (change.Property == CubeStyleProperty)
        {
            ApplyColors();
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
            Send(viewer => viewer.Redraw());
        });
    }

    private void OnViewerCreated()
    {
        ApplyColors();
        // The first frame also tells whether the view could be shown in the window.
        RequestFrame();
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
