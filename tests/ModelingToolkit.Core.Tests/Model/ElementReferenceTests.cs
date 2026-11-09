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
}
