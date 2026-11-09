using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class ElementReferenceTests
{
    [Fact]
    public void References_are_equal_when_their_entities_kind_and_index_are()
    {
        var shape = new BrepEntity(new NoKernel(), new NoShape());
        var block = new Block("Part") { Entities = { shape } };
        var first = new BlockReference(block, Transform.Identity);
        var second = new BlockReference(block, Transform.Translation(10, 0, 0));
        var edge = new ElementReference([first, shape], ElementKind.Edge, 3);

        Assert.Equal(edge, new ElementReference(new List<Entity> { first, shape }, ElementKind.Edge, 3));
        Assert.Equal(edge.GetHashCode(), new ElementReference([first, shape], ElementKind.Edge, 3).GetHashCode());
        Assert.NotEqual(edge, new ElementReference([second, shape], ElementKind.Edge, 3)); // another placement
        Assert.NotEqual(edge, new ElementReference([first, shape], ElementKind.Face, 3));
        Assert.NotEqual(edge, new ElementReference([first, shape], ElementKind.Edge, 4));
        Assert.Same(shape, edge.Entity);
    }

    [Fact]
    public void A_reference_needs_a_path_to_a_shape_and_an_index()
    {
        var shape = new BrepEntity(new NoKernel(), new NoShape());
        var mesh = new MeshEntity([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new Triangle(0, 1, 2)]);

        Assert.Throws<ArgumentException>(() => new ElementReference([], ElementKind.Face, 0));
        Assert.Throws<ArgumentException>(() => new ElementReference([mesh], ElementKind.Face, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElementReference([shape], ElementKind.Face, -1));
    }

    private sealed class NoShape : IKernelShape
    {
        public void Dispose()
        {
        }
    }

    private sealed class NoKernel : IGeometryKernel
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

        public bool IsValid(IKernelShape shape) => throw new NotSupportedException();

        public byte[] Serialize(IKernelShape shape) => throw new NotSupportedException();

        public IKernelShape Deserialize(ReadOnlySpan<byte> data) => throw new NotSupportedException();

        public ShapeTessellation Tessellate(IKernelShape shape, double linearDeflection, double angularDeflection = 0.5,
            IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public BoundingBox GetBoundingBox(IKernelShape shape) => throw new NotSupportedException();
    }
}
