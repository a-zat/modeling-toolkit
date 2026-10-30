namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// Exact-geometry operations (B-rep solids, NURBS, booleans, tessellation...) provided by a geometry kernel.
/// The core depends only on this interface; implementations wrap a specific kernel, such as OpenCASCADE.
/// </summary>
/// <remarks>Lengths are in millimetres.</remarks>
public interface IGeometryKernel
{
    KernelInfo Info { get; }

    IKernelShape MakeBox(double dx, double dy, double dz);

    double ComputeVolume(IKernelShape shape);
}
