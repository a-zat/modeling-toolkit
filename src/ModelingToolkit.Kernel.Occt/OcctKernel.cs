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

    public unsafe EdgeInfo GetEdgeInfo(IKernelShape shape, int edge)
    {
        NativeMethods.ShapeEdgeInfo(Unwrap(shape), edge, out NativeEdgeInfo info).ThrowIfOutOfRange(nameof(edge));
        var kind = (CurveKind)info.Kind;
        CircleInfo? circle = kind is CurveKind.Circle or CurveKind.Arc
            ? new CircleInfo(PointOf(info.Centre), VectorOf(info.Normal), info.Radius)
            : null;
        return new EdgeInfo(kind, info.Closed != 0, info.Length, PointOf(info.Start), PointOf(info.End),
            PointOf(info.Middle), VectorOf(info.StartTangent), VectorOf(info.EndTangent), circle);

        static Point3d PointOf(double* values) => new(values[0], values[1], values[2]);
        static Vector3d VectorOf(double* values) => new(values[0], values[1], values[2]);
    }

    public unsafe ShapeEdit AddEdges(IKernelShape? shape, IReadOnlyList<Curve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (curves.Count == 0)
        {
            throw new ArgumentException("There are no curves to add.", nameof(curves));
        }

        var native = new NativeCurve[curves.Count];
        for (int i = 0; i < curves.Count; i++)
        {
            native[i] = ToNative(curves[i]);
        }

        EditHandle edit;
        NativeStatus status = shape is null
            ? NativeMethods.ShapeAddEdgesToNothing(0, native, native.Length, out edit)
            : NativeMethods.ShapeAddEdges(Unwrap(shape), native, native.Length, out edit);
        status.ThrowIfFailed();
        return Take(edit);
    }

    public ShapeEdit PushPull(IKernelShape shape, int face, double distance)
    {
        NativeMethods.ShapePushPull(Unwrap(shape), face, distance, out EditHandle edit).ThrowIfFailed();
        return Take(edit);
    }

    public ShapeEdit RemoveElements(IKernelShape shape, IReadOnlyList<int> faces, IReadOnlyList<int> edges)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(edges);
        int[] faceIndices = [.. faces];
        int[] edgeIndices = [.. edges];
        NativeMethods.ShapeRemoveElements(Unwrap(shape), faceIndices, faceIndices.Length, edgeIndices,
            edgeIndices.Length, out EditHandle edit).ThrowIfFailed();
        return Take(edit);
    }

    public IReadOnlyList<int> GetFaceEdges(IKernelShape shape, int face)
    {
        NativeMethods.ShapeFaceEdges(Unwrap(shape), face, [], 0, out int count).ThrowIfOutOfRange(nameof(face));
        int[] edges = new int[count];
        NativeMethods.ShapeFaceEdges(Unwrap(shape), face, edges, count, out _).ThrowIfFailed();
        return edges;
    }

    private static unsafe ShapeEdit Take(EditHandle edit)
    {
        using (edit)
        {
            NativeMethods.EditGetFaceOrigins(edit, out int* origins, out int count).ThrowIfFailed();
            int[] faceOrigins = new ReadOnlySpan<int>(origins, count).ToArray();
            NativeMethods.EditGetShape(edit, out ShapeHandle made).ThrowIfFailed();
            return new ShapeEdit(new OcctShape(made), faceOrigins);
        }
    }

    private static unsafe NativeCurve ToNative(Curve curve)
    {
        (int kind, Point3d first, Point3d second, Point3d third, double radius) = curve switch
        {
            LineCurve line => (0, line.Start, line.End, default, 0.0),
            CircleCurve circle => (1, circle.Centre, new Point3d(circle.Normal.X, circle.Normal.Y, circle.Normal.Z),
                default(Point3d), circle.Radius),
            ArcCurve arc => (2, arc.Start, arc.Through, arc.End, 0.0),
            _ => throw new ArgumentException(
                $"A curve of type {curve?.GetType().Name ?? "null"} cannot be drawn.", nameof(curve)),
        };

        var native = new NativeCurve { Kind = kind, Radius = radius };
        Point3d[] points = [first, second, third];
        for (int i = 0; i < 3; i++)
        {
            native.Points[3 * i] = points[i].X;
            native.Points[3 * i + 1] = points[i].Y;
            native.Points[3 * i + 2] = points[i].Z;
        }
        return native;
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
