using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class AnimationTests
{
    private const double Tolerance = 1e-9;

    private static readonly double[] QuarterTurnAroundZ = [0, 0, Math.Sin(Math.PI / 4), Math.Cos(Math.PI / 4)];

    [Fact]
    public void A_linear_translation_moves_along_the_axes_of_the_block_between_its_keys()
    {
        // The block is turned a quarter around Z and moved: its X axis is the Y axis of the model.
        BlockReference drawer = Reference(Transform.Translation(100, 0, 0) *
                                          Transform.Rotation(default, Vector3d.UnitZ, Math.PI / 2));
        var animation = new Animation("Open", [
            new AnimationChannel(drawer, AnimatedProperty.Translation, AnimationInterpolation.Linear,
                [0, 2], [0, 0, 0, 400, 0, 0]),
        ]);

        Assert.Equal(2, animation.Duration);
        AssertPoint(new Point3d(100, 0, 0), Place(animation, drawer, -1, default));
        AssertPoint(new Point3d(100, 100, 0), Place(animation, drawer, 0.5, default));
        AssertPoint(new Point3d(100, 400, 0), Place(animation, drawer, 2, default));
        AssertPoint(new Point3d(100, 400, 0), Place(animation, drawer, 5, default));
    }

    [Fact]
    public void A_rotation_turns_around_the_origin_of_the_block_along_the_shortest_arc()
    {
        // A door hinged at its origin, 1 m from it along X.
        BlockReference door = Reference(Transform.Translation(0, 0, 500));
        var animation = new Animation("Open", [
            new AnimationChannel(door, AnimatedProperty.Rotation, AnimationInterpolation.Linear,
                [0, 1], [0, 0, 0, 1, .. QuarterTurnAroundZ]),
        ]);

        AssertPoint(new Point3d(1000, 0, 500), Place(animation, door, 0, new Point3d(1000, 0, 0)));
        double half = 1000 * Math.Sqrt(0.5);
        AssertPoint(new Point3d(half, half, 500), Place(animation, door, 0.5, new Point3d(1000, 0, 0)));
        AssertPoint(new Point3d(0, 1000, 500), Place(animation, door, 1, new Point3d(1000, 0, 0)));
    }

    [Fact]
    public void Steps_hold_each_value_until_the_next_key_and_scales_multiply_along_the_axes()
    {
        BlockReference blind = Reference(Transform.Identity);
        var animation = new Animation("Roll up", [
            new AnimationChannel(blind, AnimatedProperty.Scale, AnimationInterpolation.Step,
                [0, 1], [1, 1, 1, 1, 1, 0.25]),
        ]);

        AssertPoint(new Point3d(10, 10, 100), Place(animation, blind, 0.99, new Point3d(10, 10, 100)));
        AssertPoint(new Point3d(10, 10, 25), Place(animation, blind, 1, new Point3d(10, 10, 100)));
    }

    [Fact]
    public void A_cubic_spline_follows_the_tangents_of_its_keys()
    {
        BlockReference part = Reference(Transform.Identity);
        // From 0 to 1 along X in one second, leaving and arriving with no speed: halfway at half the time.
        var animation = new Animation("Ease", [
            new AnimationChannel(part, AnimatedProperty.Translation, AnimationInterpolation.CubicSpline,
                [0, 1], [0, 0, 0, 0, 0, 0, 0, 0, 0, /* second key */ 0, 0, 0, 1, 0, 0, 0, 0, 0]),
        ]);

        AssertPoint(new Point3d(0.5, 0, 0), Place(animation, part, 0.5, default));
        // s = 0.25: 3s² - 2s³
        AssertPoint(new Point3d(3 * 0.0625 - 2 * 0.015625, 0, 0), Place(animation, part, 0.25, default));
    }

    [Fact]
    public void The_motion_follows_the_reference_where_it_is_moved()
    {
        BlockReference door = Reference(Transform.Identity);
        var animation = new Animation("Open", [
            new AnimationChannel(door, AnimatedProperty.Rotation, AnimationInterpolation.Linear,
                [0, 1], [0, 0, 0, 1, .. QuarterTurnAroundZ]),
        ]);

        door.Transform = Transform.Translation(0, 5000, 0);

        AssertPoint(new Point3d(0, 6000, 0), Place(animation, door, 1, new Point3d(1000, 0, 0)));
    }

    [Fact]
    public void The_channels_of_one_reference_are_composed_and_those_of_others_kept_apart()
    {
        BlockReference door = Reference(Transform.Identity), drawer = Reference(Transform.Identity);
        var animation = new Animation("Open all", [
            new AnimationChannel(door, AnimatedProperty.Rotation, AnimationInterpolation.Linear,
                [0, 1], [0, 0, 0, 1, .. QuarterTurnAroundZ]),
            new AnimationChannel(door, AnimatedProperty.Translation, AnimationInterpolation.Linear,
                [0, 1], [0, 0, 0, 0, 0, 10]),
            new AnimationChannel(drawer, AnimatedProperty.Translation, AnimationInterpolation.Linear,
                [0, 3], [0, 0, 0, 300, 0, 0]),
        ]);

        Assert.Equal([door, drawer], animation.Targets);
        Assert.Equal(3, animation.Duration);
        IReadOnlyDictionary<BlockReference, Transform> pose = animation.PoseAt(1);
        // The rotation comes first, then the translation, as with the transformation of a glTF node.
        AssertPoint(new Point3d(0, 1000, 10), pose[door].Apply(new Point3d(1000, 0, 0)));
        AssertPoint(new Point3d(100, 0, 0), pose[drawer].Apply(default));
    }

    [Fact]
    public void Channels_refuse_keys_out_of_order_and_values_of_the_wrong_size()
    {
        BlockReference part = Reference(Transform.Identity);

        Assert.Throws<ArgumentException>(() => new AnimationChannel(
            part, AnimatedProperty.Translation, AnimationInterpolation.Linear, [1, 0], [0, 0, 0, 1, 1, 1]));
        Assert.Throws<ArgumentException>(() => new AnimationChannel(
            part, AnimatedProperty.Rotation, AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 1, 0, 0]));
        Assert.Throws<ArgumentException>(() => new AnimationChannel(
            part, AnimatedProperty.Scale, AnimationInterpolation.Step, [], []));
        Assert.Throws<ArgumentException>(() => new Animation("Empty", []));
    }

    [Fact]
    public void Quaternions_and_rotation_matrices_convert_both_ways()
    {
        Transform rotation = Transform.Rotation(default, new Vector3d(1, 2, 3), 2.5);

        double[] q = Quaternions.FromTransform(rotation);
        Transform back = Quaternions.ToTransform(q);

        Point3d point = new(4, -5, 6);
        AssertPoint(rotation.Apply(point), back.Apply(point));
    }

    private static BlockReference Reference(Transform transform) => new(new Block("Part"), transform);

    private static Point3d Place(Animation animation, BlockReference target, double time, Point3d point) =>
        animation.PoseAt(time)[target].Apply(point);

    private static void AssertPoint(Point3d expected, Point3d actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }
}
