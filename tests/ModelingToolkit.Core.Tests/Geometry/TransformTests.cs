using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Tests.Geometry;

public sealed class TransformTests
{
    // 90° counter-clockwise around Z: X goes to Y.
    private static readonly Transform RotationZ90 = new(
        0, -1, 0, 0,
        1, 0, 0, 0,
        0, 0, 1, 0);

    [Fact]
    public void Identity_leaves_points_unchanged()
    {
        Assert.Equal(new Point3d(1, 2, 3), Transform.Identity.Apply(new Point3d(1, 2, 3)));
        Assert.True(Transform.Identity.IsIdentity);
    }

    [Fact]
    public void Translation_moves_points()
    {
        Transform translation = Transform.Translation(10, 20, 30);

        Assert.Equal(new Point3d(11, 22, 33), translation.Apply(new Point3d(1, 2, 3)));
        Assert.False(translation.IsIdentity);
    }

    [Fact]
    public void Composition_applies_the_right_operand_first()
    {
        Transform translation = Transform.Translation(10, 0, 0);
        var point = new Point3d(1, 0, 0);

        Assert.Equal(new Point3d(10, 1, 0), (translation * RotationZ90).Apply(point));
        Assert.Equal(new Point3d(0, 11, 0), (RotationZ90 * translation).Apply(point));
    }
}
