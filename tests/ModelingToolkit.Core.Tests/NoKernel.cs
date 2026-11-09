using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Tests;

/// <summary>A shape of <see cref="NoKernel"/>, for the tests that only need B-rep entities.</summary>
internal sealed class NoShape : IKernelShape
{
    public void Dispose()
    {
    }
}

/// <summary>A kernel that does nothing, for the B-rep entities of the tests.</summary>
internal sealed class NoKernel : IGeometryKernel
{
    public KernelInfo Info => new("None", "0");

    public IKernelShape MakeBox(double dx, double dy, double dz) => throw new NotSupportedException();

    public IKernelShape MakeCylinder(double radius, double height) => throw new NotSupportedException();

    public IKernelShape MakeSphere(double radius) => throw new NotSupportedException();

    public IKernelShape MakeCone(double bottomRadius, double topRadius, double height) =>
        throw new NotSupportedException();

    public IKernelShape MakeTorus(double majorRadius, double minorRadius) => throw new NotSupportedException();

    public IKernelShape Place(IKernelShape shape, Transform placement) => throw new NotSupportedException();

    public IKernelShape Union(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IKernelShape Subtract(IKernelShape shape, IKernelShape tool, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IKernelShape Intersect(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public double ComputeVolume(IKernelShape shape) => throw new NotSupportedException();

    public bool IsValid(IKernelShape shape) => true;

    public ShapeTopology CountElements(IKernelShape shape) => throw new NotSupportedException();

    public FaceInfo GetFaceInfo(IKernelShape shape, int face) => throw new NotSupportedException();

    public EdgeInfo GetEdgeInfo(IKernelShape shape, int edge) => throw new NotSupportedException();

    public ShapeEdit AddEdges(IKernelShape? shape, IReadOnlyList<Curve> curves) => throw new NotSupportedException();

    public ShapeEdit PushPull(IKernelShape shape, int face, double distance) => throw new NotSupportedException();

    public IKernelShape Scale(IKernelShape shape, Point3d centre, double factor) => throw new NotSupportedException();

    public ShapeEdit RemoveElements(IKernelShape shape, IReadOnlyList<int> faces, IReadOnlyList<int> edges) =>
        throw new NotSupportedException();

    public IReadOnlyList<int> GetFaceEdges(IKernelShape shape, int face) => throw new NotSupportedException();

    public Point3d GetVertexPoint(IKernelShape shape, int vertex) => throw new NotSupportedException();

    public byte[] Serialize(IKernelShape shape) => throw new NotSupportedException();

    public IKernelShape Deserialize(ReadOnlySpan<byte> data) => throw new NotSupportedException();

    public ShapeTessellation Tessellate(IKernelShape shape, double linearDeflection, double angularDeflection = 0.5,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public BoundingBox GetBoundingBox(IKernelShape shape) => throw new NotSupportedException();
}
