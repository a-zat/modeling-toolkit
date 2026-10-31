namespace ModelingToolkit.Gui.Viewport.Interop;

/// <summary>Mirror of <c>mtk_status</c> in the native header.</summary>
internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    KernelError = 2,
    InternalError = 3,
}

internal static class NativeStatusExtensions
{
    /// <summary>Throws the exception matching a failed viewer call, with the message recorded by the viewer.</summary>
    /// <remarks>Must be called right after the call, on the same thread: the native error message is per thread.</remarks>
    public static void ThrowIfFailed(this NativeStatus status)
    {
        if (status == NativeStatus.Ok)
        {
            return;
        }

        string message = NativeMethods.LastErrorMessage();
        throw status switch
        {
            NativeStatus.InvalidArgument => new ArgumentException(message),
            _ => new ViewerException(message),
        };
    }
}
