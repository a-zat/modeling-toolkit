using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Gui.Tests;

/// <summary>A kernel whose shapes have a size but cannot be turned into triangles; it supports nothing else.</summary>
internal sealed class FailingKernel : IGeometryKernel
{
    public const string Reason = "The shape has no faces to turn into triangles.";

    public KernelInfo Info { get; } = new("Failing", "1.0");

    public IKernelShape MakeBox(double dx, double dy, double dz) => new Shape();

    public ShapeTessellation Tessellate(IKernelShape shape, double linearDeflection, double angularDeflection = 0.5,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        throw new GeometryKernelException(Reason);

    public BoundingBox GetBoundingBox(IKernelShape shape) => new(new Point3d(0, 0, 0), new Point3d(1, 1, 1));

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

    public bool IsValid(IKernelShape shape) => throw new NotSupportedException();

        public FaceInfo GetFaceInfo(IKernelShape shape, int face) => throw new NotSupportedException();

        public double GetEdgeLength(IKernelShape shape, int edge) => throw new NotSupportedException();

        public Point3d GetVertexPoint(IKernelShape shape, int vertex) => throw new NotSupportedException();

    public byte[] Serialize(IKernelShape shape) => throw new NotSupportedException();

    public IKernelShape Deserialize(ReadOnlySpan<byte> data) => throw new NotSupportedException();

    private sealed class Shape : IKernelShape
    {
        public void Dispose()
        {
        }
    }
}
