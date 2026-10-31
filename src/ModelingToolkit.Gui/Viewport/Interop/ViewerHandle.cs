using Avalonia.Threading;
using Microsoft.Win32.SafeHandles;

namespace ModelingToolkit.Gui.Viewport.Interop;

/// <summary>Owns an <c>mtk_viewer</c> handle and releases it when disposed or finalized.</summary>
internal sealed class ViewerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by the P/Invoke marshaller, which then sets the native handle.
    public ViewerHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        // The viewer destroys its native view, which belongs to the thread of the user interface: a handle finalized
        // on another thread is released there.
        nint viewer = handle;
        if (Dispatcher.UIThread.CheckAccess())
        {
            NativeMethods.Release(viewer);
        }
        else
        {
            Dispatcher.UIThread.Post(() => NativeMethods.Release(viewer));
        }

        return true;
    }
}
