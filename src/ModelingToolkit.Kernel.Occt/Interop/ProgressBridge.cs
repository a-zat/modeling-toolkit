using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

/// <summary>Mirror of <c>mtk_progress</c> in the native header.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeProgress
{
    public delegate* unmanaged[Cdecl]<double, nint, int> Report;
    public nint UserData;
}

/// <summary>
/// Passes the progress of a native operation to an <see cref="IProgress{T}"/>, and stops the operation when a token is
/// cancelled. The native operation may call back from threads of its own, one at a time.
/// </summary>
internal sealed unsafe class ProgressBridge : IDisposable
{
    private readonly IProgress<double>? _progress;
    private readonly CancellationToken _cancellationToken;
    private GCHandle _self;
    private Exception? _failure;

    /// <exception cref="OperationCanceledException">The token is already cancelled.</exception>
    public ProgressBridge(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _progress = progress;
        _cancellationToken = cancellationToken;
        if (progress is not null || cancellationToken.CanBeCanceled)
        {
            _self = GCHandle.Alloc(this);
            Native = new NativeProgress { Report = &Report, UserData = GCHandle.ToIntPtr(_self) };
        }
    }

    /// <summary>The progress to pass to the native operation; meaningful only if <see cref="IsUsed"/>.</summary>
    public NativeProgress Native { get; }

    /// <summary>Whether there is anything to report to or a cancellation to watch; null is passed otherwise.</summary>
    public bool IsUsed => _self.IsAllocated;

    /// <summary>
    /// Throws for a failed native call, right after it: the exception thrown by the progress, if it threw, an
    /// <see cref="OperationCanceledException"/> if the operation was stopped, otherwise that of the status.
    /// </summary>
    public void ThrowIfFailed(NativeStatus status)
    {
        if (_failure is not null)
        {
            ExceptionDispatchInfo.Throw(_failure);
        }
        if (status == NativeStatus.Cancelled)
        {
            throw new OperationCanceledException(_cancellationToken);
        }
        status.ThrowIfFailed();
    }

    public void Dispose()
    {
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    // No exception may leave a function called by native code: one from the progress stops the operation instead, and
    // is thrown once it has returned.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Report(double done, nint userData)
    {
        var bridge = (ProgressBridge)GCHandle.FromIntPtr(userData).Target!;
        try
        {
            bridge._progress?.Report(done);
            return bridge._cancellationToken.IsCancellationRequested ? 1 : 0;
        }
        catch (Exception exception)
        {
            bridge._failure ??= exception;
            return 1;
        }
    }
}
