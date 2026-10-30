using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Owns an <c>mtk_scene</c> handle and releases it when disposed or finalized.</summary>
internal sealed class SceneHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public SceneHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.SceneRelease(handle);
        return true;
    }
}
