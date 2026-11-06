using System.Runtime.InteropServices;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
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

    // The values are those of mtk_boolean_operation in the native header.
    private enum BooleanOperation
    {
        Union,
        Difference,
        Intersection,
    }

    public IKernelShape Union(IKernelShape first, IKernelShape second) =>
        Combine(first, second, BooleanOperation.Union);

    public IKernelShape Subtract(IKernelShape shape, IKernelShape tool) =>
        Combine(shape, tool, BooleanOperation.Difference);

    public IKernelShape Intersect(IKernelShape first, IKernelShape second) =>
        Combine(first, second, BooleanOperation.Intersection);

    private static OcctShape Combine(IKernelShape first, IKernelShape second, BooleanOperation operation)
    {
        NativeMethods.ShapeBoolean(Unwrap(first), Unwrap(second), (int)operation, out ShapeHandle handle)
            .ThrowIfFailed();
        return new OcctShape(handle);
    }

    public double ComputeVolume(IKernelShape shape)
    {
        NativeMethods.ShapeVolume(Unwrap(shape), out double volume).ThrowIfFailed();
        return volume;
    }

    public unsafe byte[] Serialize(IKernelShape shape)
    {
        NativeMethods.ShapeWrite(Unwrap(shape), out BytesHandle bytes).ThrowIfFailed();
        using (bytes)
        {
            NativeMethods.BytesGet(bytes, out byte* data, out long size).ThrowIfFailed();
            return new ReadOnlySpan<byte>(data, checked((int)size)).ToArray();
        }
    }

    public IKernelShape Deserialize(ReadOnlySpan<byte> data)
    {
        NativeMethods.ShapeRead(data, data.Length, out ShapeHandle handle).ThrowIfFailed();
        return new OcctShape(handle);
    }

    public unsafe ShapeTessellation Tessellate(
        IKernelShape shape, double linearDeflection, double angularDeflection = 0.5)
    {
        NativeMethods.ShapeTessellate(Unwrap(shape), linearDeflection, angularDeflection, out TessellationHandle handle)
            .ThrowIfFailed();
        using (handle)
        {
            NativeMethods.TessellationGetMesh(handle, out MeshInfo mesh).ThrowIfFailed();
            NativeMethods.TessellationGetFaces(handle, out int* firstTriangles, out int faceCount).ThrowIfFailed();
            NativeMethods.TessellationGetEdges(handle, out double* points, out int* firstPoints, out int edgeCount)
                .ThrowIfFailed();

            int pointCount = firstPoints[edgeCount];
            var edges = new Polylines(
                MemoryMarshal.Cast<double, Point3d>(new ReadOnlySpan<double>(points, 3 * pointCount)).ToArray(),
                new ReadOnlySpan<int>(firstPoints, edgeCount + 1).ToArray());
            return new ShapeTessellation(
                mesh.ToMeshEntity(), new ReadOnlySpan<int>(firstTriangles, faceCount + 1).ToArray(), edges);
        }
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
