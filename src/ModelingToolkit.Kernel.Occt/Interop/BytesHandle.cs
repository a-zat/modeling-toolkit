using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Owns an <c>mtk_bytes</c> handle and releases it when disposed or finalized.</summary>
internal sealed class BytesHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public BytesHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.BytesRelease(handle);
        return true;
    }
}
