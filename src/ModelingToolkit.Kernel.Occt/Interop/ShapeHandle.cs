using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Owns an <c>mtk_shape</c> handle and releases it when disposed or finalized.</summary>
internal sealed class ShapeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public ShapeHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ShapeRelease(handle);
        return true;
    }
}
