using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Tests.Geometry;

public sealed class BoundingBoxTests
{
    [Fact]
    public void Empty_box_is_neutral_for_union()
    {
        var box = new BoundingBox(new Point3d(0, 0, 0), new Point3d(1, 1, 1));

        Assert.True(BoundingBox.Empty.IsEmpty);
        Assert.Equal(box, box.Union(BoundingBox.Empty));
        Assert.Equal(box, BoundingBox.Empty.Union(box));
    }

    [Fact]
    public void Including_points_grows_the_box()
    {
        BoundingBox box = BoundingBox.Empty.Include(new Point3d(1, 5, -2)).Include(new Point3d(-3, 2, 4));

        Assert.Equal(new BoundingBox(new Point3d(-3, 2, -2), new Point3d(1, 5, 4)), box);
    }

    [Fact]
    public void Transformed_box_contains_the_transformed_corners()
    {
        var box = new BoundingBox(new Point3d(0, 0, 0), new Point3d(2, 1, 1));
        var rotationZ90 = new Transform(
            0, -1, 0, 0,
            1, 0, 0, 0,
            0, 0, 1, 0);

        Assert.Equal(new BoundingBox(new Point3d(-1, 0, 0), new Point3d(0, 2, 1)), box.Transform(rotationZ90));
        Assert.True(BoundingBox.Empty.Transform(rotationZ90).IsEmpty);
    }
}
