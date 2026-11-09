using System.Runtime.InteropServices;
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

    // The values are those of mtk_boolean_operation in the native header.
    private enum BooleanOperation
    {
        Union,
        Difference,
        Intersection,
    }

    public IKernelShape Union(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Combine(first, second, BooleanOperation.Union, progress, cancellationToken);

    public IKernelShape Subtract(IKernelShape shape, IKernelShape tool, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Combine(shape, tool, BooleanOperation.Difference, progress, cancellationToken);

    public IKernelShape Intersect(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Combine(first, second, BooleanOperation.Intersection, progress, cancellationToken);

    private static unsafe OcctShape Combine(IKernelShape first, IKernelShape second, BooleanOperation operation,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ShapeHandle a = Unwrap(first), b = Unwrap(second);
        using var bridge = new ProgressBridge(progress, cancellationToken);
        NativeProgress native = bridge.Native;
        NativeStatus status = NativeMethods.ShapeBoolean(
            a, b, (int)operation, bridge.IsUsed ? &native : null, out ShapeHandle handle);
        ThrowIfFailed(bridge, status, handle);
        return new OcctShape(handle);
    }

    public double ComputeVolume(IKernelShape shape)
    {
        NativeMethods.ShapeVolume(Unwrap(shape), out double volume).ThrowIfFailed();
        return volume;
    }

    public bool IsValid(IKernelShape shape)
    {
        NativeMethods.ShapeIsValid(Unwrap(shape), out int valid).ThrowIfFailed();
        return valid != 0;
    }

    public ShapeTopology CountElements(IKernelShape shape)
    {
        Span<int> counts = stackalloc int[4];
        NativeMethods.ShapeCountElements(Unwrap(shape), counts).ThrowIfFailed();
        return new ShapeTopology(counts[0], counts[1], counts[2], counts[3]);
    }

    public FaceInfo GetFaceInfo(IKernelShape shape, int face)
    {
        Span<double> origin = stackalloc double[3];
        Span<double> normal = stackalloc double[3];
        NativeMethods.ShapeFaceInfo(Unwrap(shape), face, out double area, out int planar, origin, normal)
            .ThrowIfOutOfRange(nameof(face));
        if (planar == 0)
        {
            return new FaceInfo(area, null);
        }

        // The point of the plane closest to the origin is the normal times the offset.
        double offset = origin[0] * normal[0] + origin[1] * normal[1] + origin[2] * normal[2];
        return new FaceInfo(area, new Plane(new Vector3d(normal[0], normal[1], normal[2]), offset));
    }

    public double GetEdgeLength(IKernelShape shape, int edge)
    {
        NativeMethods.ShapeEdgeLength(Unwrap(shape), edge, out double length).ThrowIfOutOfRange(nameof(edge));
        return length;
    }

    public Point3d GetVertexPoint(IKernelShape shape, int vertex)
    {
        Span<double> point = stackalloc double[3];
        NativeMethods.ShapeVertexPoint(Unwrap(shape), vertex, point).ThrowIfOutOfRange(nameof(vertex));
        return new Point3d(point[0], point[1], point[2]);
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

    public unsafe ShapeTessellation Tessellate(IKernelShape shape, double linearDeflection,
        double angularDeflection = 0.5, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ShapeHandle unwrapped = Unwrap(shape);
        TessellationHandle handle;
        using (var bridge = new ProgressBridge(progress, cancellationToken))
        {
            NativeProgress native = bridge.Native;
            NativeStatus status = NativeMethods.ShapeTessellate(
                unwrapped, linearDeflection, angularDeflection, bridge.IsUsed ? &native : null, out handle);
            ThrowIfFailed(bridge, status, handle);
        }
        using (handle)
        {
            NativeMethods.TessellationGetMesh(handle, out MeshInfo mesh).ThrowIfFailed();
            NativeMethods.TessellationGetFaces(handle, out int* firstTriangles, out int faceCount).ThrowIfFailed();
            NativeMethods.TessellationGetEdges(handle, out double* points, out int* firstPoints, out int edgeCount)
                .ThrowIfFailed();

            NativeMethods.TessellationGetEdgeIndices(handle, out int* edgeIndices).ThrowIfFailed();
            NativeMethods.TessellationGetVertices(handle, out double* vertexPoints, out int vertexCount).ThrowIfFailed();

            int pointCount = firstPoints[edgeCount];
            var edges = new Polylines(
                MemoryMarshal.Cast<double, Point3d>(new ReadOnlySpan<double>(points, 3 * pointCount)).ToArray(),
                new ReadOnlySpan<int>(firstPoints, edgeCount + 1).ToArray());
            return new ShapeTessellation(
                mesh.ToMeshEntity(), new ReadOnlySpan<int>(firstTriangles, faceCount + 1).ToArray(), edges,
                new ReadOnlySpan<int>(edgeIndices, edgeCount).ToArray(),
                MemoryMarshal.Cast<double, Point3d>(new ReadOnlySpan<double>(vertexPoints, 3 * vertexCount)).ToArray());
        }
    }

    public BoundingBox GetBoundingBox(IKernelShape shape)
    {
        Span<double> bounds = stackalloc double[6];
        NativeMethods.ShapeBounds(Unwrap(shape), bounds).ThrowIfFailed();
        return new BoundingBox(
            new Point3d(bounds[0], bounds[1], bounds[2]), new Point3d(bounds[3], bounds[4], bounds[5]));
    }

    // The progress may throw after the operation has made its result: the result is then released at once, not left to
    // the finalizer.
    private static void ThrowIfFailed(ProgressBridge bridge, NativeStatus status, SafeHandle result)
    {
        try
        {
            bridge.ThrowIfFailed(status);
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static ShapeHandle Unwrap(IKernelShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape is OcctShape occtShape
            ? occtShape.Handle
            : throw new ArgumentException($"The shape was not created by {nameof(OcctKernel)}.", nameof(shape));
    }
}
