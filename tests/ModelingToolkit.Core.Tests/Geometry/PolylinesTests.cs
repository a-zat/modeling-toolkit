using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Tests.Geometry;

public sealed class PolylinesTests
{
    [Fact]
    public void Each_polyline_has_its_own_points()
    {
        Point3d[] points = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(5, 5, 5), new(6, 6, 6)];

        var polylines = new Polylines(points, [0, 3, 5]);

        Assert.Equal(2, polylines.Count);
        Assert.Equal(points[..3], polylines[0].ToArray());
        Assert.Equal(points[3..], polylines[1].ToArray());
        Assert.Equal(0, Polylines.Empty.Count);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 3 })]
    [InlineData(new[] { 0, 2 })]
    [InlineData(new[] { 0, 1, 3 })]
    public void Indices_out_of_range_or_too_short_polylines_are_refused(int[] firstPoints)
    {
        Point3d[] points = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)];

        Assert.Throws<ArgumentException>(() => new Polylines(points, firstPoints));
    }
}
