using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Owns an <c>mtk_tessellation</c> handle and releases it when disposed or finalized.</summary>
internal sealed class TessellationHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public TessellationHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.TessellationRelease(handle);
        return true;
    }
}
