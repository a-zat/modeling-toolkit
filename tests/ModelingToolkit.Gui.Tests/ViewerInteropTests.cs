using System.Runtime.CompilerServices;
using Avalonia.Input;
using Avalonia.Input.Raw;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The native viewer ships with the application, with the layouts and the flags its interop declares.</summary>
public sealed class ViewerInteropTests
{
    [Fact]
    public void TheNativeViewerHasTheExpectedAbiVersion() =>
        Assert.Equal(NativeMethods.AbiVersion, NativeMethods.ViewerAbiVersion());

    // Same size as the static_assert in native/occt-viewer/src/viewer.cpp: a mismatch would corrupt memory.
    [Fact]
    public void ColoursHaveTheNativeSize() => Assert.Equal(36, Unsafe.SizeOf<ViewerColors>());

    [Fact]
    public void PressedButtonsBecomeFlagsOfTheViewer()
    {
        var properties = new PointerPointProperties(
            RawInputModifiers.LeftMouseButton | RawInputModifiers.RightMouseButton, PointerUpdateKind.Other);

        Assert.Equal(NativeMouseButtons.Left | NativeMouseButtons.Right, OcctViewer.ToNative(properties));
        Assert.Equal(NativeMouseButtons.None, OcctViewer.ToNative(new PointerPointProperties()));
    }

    [Fact]
    public void ModifierKeysBecomeFlagsOfTheViewer()
    {
        Assert.Equal(NativeModifierKeys.None, OcctViewer.ToNative(KeyModifiers.None));
        Assert.Equal(NativeModifierKeys.Shift | NativeModifierKeys.Meta,
            OcctViewer.ToNative(KeyModifiers.Shift | KeyModifiers.Meta));
        Assert.Equal(NativeModifierKeys.Control | NativeModifierKeys.Alt,
            OcctViewer.ToNative(KeyModifiers.Control | KeyModifiers.Alt));
    }
}
