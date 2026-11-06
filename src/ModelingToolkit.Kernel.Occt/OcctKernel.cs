using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt;

/// <summary><see cref="IGeometryKernel"/> implemented with OpenCASCADE, through the native bridge.</summary>
public sealed class OcctKernel : IGeometryKernel
{
    public OcctKernel()
    {
        int nativeAbiVersion = NativeMethods.BridgeAbiVersion();
        if (nativeAbiVersion != NativeMethods.AbiVersion)
        {
            throw new InvalidOperationException(
                $"The native bridge implements ABI version {nativeAbiVersion}, but version {NativeMethods.AbiVersion} " +
                "is expected: rebuild the solution.");
        }

        Info = new KernelInfo("OpenCASCADE", NativeMethods.OcctVersion());
    }

    public KernelInfo Info { get; }

    public IKernelShape MakeBox(double dx, double dy, double dz)
    {
        NativeMethods.ShapeMakeBox(dx, dy, dz, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public IKernelShape MakeCylinder(double radius, double height)
    {
        NativeMethods.ShapeMakeCylinder(radius, height, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public IKernelShape MakeSphere(double radius)
    {
        NativeMethods.ShapeMakeSphere(radius, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public IKernelShape MakeCone(double bottomRadius, double topRadius, double height)
    {
        NativeMethods.ShapeMakeCone(bottomRadius, topRadius, height, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public IKernelShape MakeTorus(double majorRadius, double minorRadius)
    {
        NativeMethods.ShapeMakeTorus(majorRadius, minorRadius, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public IKernelShape Place(IKernelShape shape, Transform placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ReadOnlySpan<double> matrix =
        [
            placement.M00, placement.M01, placement.M02, placement.M03,
            placement.M10, placement.M11, placement.M12, placement.M13,
            placement.M20, placement.M21, placement.M22, placement.M23,
        ];
        NativeMethods.ShapePlace(Unwrap(shape), matrix, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public double ComputeVolume(IKernelShape shape)
    {
        NativeMethods.ShapeVolume(Unwrap(shape), out double volume).ThrowIfFailed();
        return volume;
    }

    public BoundingBox GetBoundingBox(IKernelShape shape)
    {
        Span<double> bounds = stackalloc double[6];
        NativeMethods.ShapeBounds(Unwrap(shape), bounds).ThrowIfFailed();
        return new BoundingBox(
            new Point3d(bounds[0], bounds[1], bounds[2]), new Point3d(bounds[3], bounds[4], bounds[5]));
    }

    private static ShapeHandle Unwrap(IKernelShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape is OcctShape occtShape
            ? occtShape.Handle
            : throw new ArgumentException($"The shape was not created by {nameof(OcctKernel)}.", nameof(shape));
    }
}
