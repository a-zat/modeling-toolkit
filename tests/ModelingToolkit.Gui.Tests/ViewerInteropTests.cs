using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The native viewer ships with the application, with the layouts and the flags its interop declares.</summary>
public sealed class ViewerInteropTests
{
    [Fact]
    public void TheNativeViewerHasTheExpectedAbiVersion() =>
        Assert.Equal(NativeMethods.AbiVersion, NativeMethods.ViewerAbiVersion());

    // Same sizes as the static_asserts in native/occt-viewer/src/viewer.cpp: a mismatch would corrupt memory.
    [Fact]
    public void ColoursHaveTheNativeSize() => Assert.Equal(144, Unsafe.SizeOf<ViewerColors>());

    [Fact]
    public void MeshesAndMaterialsHaveTheNativeSizes()
    {
        Assert.Equal(IntPtr.Size == 8 ? 120 : 60, Unsafe.SizeOf<NativeMesh>());
        Assert.Equal(56, Unsafe.SizeOf<NativePick>());
        Assert.Equal(12, Unsafe.SizeOf<ViewerElement>());
        Assert.Equal(28, Unsafe.SizeOf<NativeMaterial>());
    }

    // The arrays of a mesh are passed to the viewer as arrays of doubles, floats and ints.
    [Fact]
    public void TheElementsOfMeshesAreMadeOfTheirCoordinates()
    {
        Assert.Equal(3 * sizeof(double), Unsafe.SizeOf<Point3d>());
        Assert.Equal(3 * sizeof(int), Unsafe.SizeOf<Triangle>());
        Assert.Equal(3 * sizeof(float), Unsafe.SizeOf<Vector3>());
        Assert.Equal(2 * sizeof(float), Unsafe.SizeOf<Vector2>());
    }

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
