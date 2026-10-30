using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt;

internal sealed class OcctShape(ShapeHandle handle) : IKernelShape
{
    public ShapeHandle Handle { get; } = handle;

    public void Dispose() => Handle.Dispose();
}
