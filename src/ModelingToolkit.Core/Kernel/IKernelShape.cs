namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// Opaque handle to a shape owned by a geometry kernel; it can only be passed back to the kernel that created it.
/// Disposing it releases the native memory immediately, otherwise the garbage collector does it eventually.
/// </summary>
public interface IKernelShape : IDisposable
{
}
