using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Kernel;

public sealed class ShapeTessellationTests
{
    private static readonly MeshEntity TwoTriangles = new(
        [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)], [new(0, 1, 2), new(0, 2, 3)]);

    [Fact]
    public void Each_face_has_its_range_of_triangles()
    {
        var tessellation = new ShapeTessellation(TwoTriangles, [0, 1, 1, 2], Polylines.Empty);

        Assert.Equal(3, tessellation.FaceCount);
        Assert.Equal(0..1, tessellation.TrianglesOf(0));
        Assert.Equal(1..1, tessellation.TrianglesOf(1));
        Assert.Equal(1..2, tessellation.TrianglesOf(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => tessellation.TrianglesOf(3));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2 })]
    [InlineData(new[] { 0, 1 })]
    [InlineData(new[] { 0, 2, 1, 2 })]
    public void Ranges_that_do_not_cover_the_triangles_in_order_are_refused(int[] firstTriangles)
    {
        Assert.Throws<ArgumentException>(() => new ShapeTessellation(TwoTriangles, firstTriangles, Polylines.Empty));
    }
}
