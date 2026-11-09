using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Owns an <c>mtk_edit</c> handle and releases it when disposed or finalized.</summary>
internal sealed class EditHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public EditHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.EditRelease(handle);
        return true;
    }
}
