using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Mirror of <c>mtk_status</c> in the native header.</summary>
internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    KernelError = 2,
    InternalError = 3,
    Cancelled = 4,
}

internal static class NativeStatusExtensions
{
    /// <summary>Throws the exception matching a failed bridge call, with the message recorded by the bridge.</summary>
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
            NativeStatus.Cancelled => new OperationCanceledException(message),
            _ => new GeometryKernelException(message),
        };
    }

    /// <summary>
    /// As <see cref="ThrowIfFailed"/>, for a call whose only argument that the bridge can refuse is an index: an
    /// <see cref="ArgumentOutOfRangeException"/> for it.
    /// </summary>
    public static void ThrowIfOutOfRange(this NativeStatus status, string paramName)
    {
        if (status == NativeStatus.InvalidArgument)
        {
            throw new ArgumentOutOfRangeException(paramName, NativeMethods.LastErrorMessage());
        }
        status.ThrowIfFailed();
    }
}
