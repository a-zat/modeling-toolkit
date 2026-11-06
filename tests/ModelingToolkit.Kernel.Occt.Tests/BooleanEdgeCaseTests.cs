using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Kernel.Occt.Tests;

/// <summary>
/// Boolean operations where shapes touch, coincide, contain each other or differ by less than the tolerance of the
/// kernel (10⁻⁷ mm): the cases where a modeller goes wrong first.
/// </summary>
public sealed class BooleanEdgeCaseTests
{
    private const double Precision = 1e-6;

    private readonly OcctKernel _kernel = new();

    [Theory]
    [InlineData(10, 0, 0)] // a face
    [InlineData(10, 10, 0)] // an edge
    [InlineData(10, 10, 10)] // a corner
    public void Boxes_that_touch_unite_into_both_volumes_and_have_nothing_in_common(double x, double y, double z)
    {
        using IKernelShape first = _kernel.MakeBox(10, 10, 10);
        using IKernelShape second = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(x, y, z));

        using IKernelShape union = _kernel.Union(first, second);
        using IKernelShape difference = _kernel.Subtract(first, second);

        Assert.Equal(2000, _kernel.ComputeVolume(union), Precision);
        Assert.Equal(1000, _kernel.ComputeVolume(difference), Precision);
        Assert.Throws<GeometryKernelException>(() => _kernel.Intersect(first, second));
    }

    [Fact]
    public void Boxes_side_by_side_unite_into_one_box()
    {
        using IKernelShape first = _kernel.MakeBox(10, 10, 10);
        using IKernelShape second = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(10, 0, 0));

        using IKernelShape union = _kernel.Union(first, second);

        ShapeTessellation tessellation = _kernel.Tessellate(union, 1);
        Assert.Equal((6, 12), (tessellation.FaceCount, tessellation.Edges.Count));
        AssertBox(new Point3d(0, 0, 0), new Point3d(20, 10, 10), _kernel.GetBoundingBox(union));
    }

    [Theory]
    [InlineData(1e-8)] // a gap
    [InlineData(-1e-8)] // an overlap
    public void Differences_below_the_tolerance_count_as_touching(double offset)
    {
        using IKernelShape first = _kernel.MakeBox(10, 10, 10);
        using IKernelShape second = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(10 + offset, 0, 0));

        using IKernelShape union = _kernel.Union(first, second);

        Assert.Equal(6, _kernel.Tessellate(union, 1).FaceCount);
        Assert.Equal(2000, _kernel.ComputeVolume(union), 1e-5); // give or take the 10⁻⁶ mm³ of the gap
        Assert.Throws<GeometryKernelException>(() => _kernel.Intersect(first, second));
    }

    [Fact]
    public void An_overlap_above_the_tolerance_is_kept()
    {
        using IKernelShape first = _kernel.MakeBox(10, 10, 10);
        using IKernelShape second = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(10 - 1e-4, 0, 0));

        using IKernelShape common = _kernel.Intersect(first, second);

        Assert.Equal(1e-2, _kernel.ComputeVolume(common), Precision);
    }

    [Fact]
    public void A_shape_combined_with_itself_is_itself_or_nothing()
    {
        using IKernelShape box = _kernel.MakeBox(10, 10, 10);

        using IKernelShape union = _kernel.Union(box, box);
        using IKernelShape common = _kernel.Intersect(box, box);

        Assert.Equal(1000, _kernel.ComputeVolume(union), Precision);
        Assert.Equal(1000, _kernel.ComputeVolume(common), Precision);
        Assert.Throws<GeometryKernelException>(() => _kernel.Subtract(box, box));
    }

    [Fact]
    public void A_shape_inside_another_is_swallowed_or_hollows_it()
    {
        using IKernelShape inner = _kernel.MakeBox(10, 10, 10);
        using IKernelShape outer = _kernel.Place(_kernel.MakeBox(30, 30, 30), Transform.Translation(-10, -10, -10));

        using IKernelShape common = _kernel.Intersect(inner, outer);
        using IKernelShape hollow = _kernel.Subtract(outer, inner);

        Assert.Throws<GeometryKernelException>(() => _kernel.Subtract(inner, outer));
        Assert.Equal(1000, _kernel.ComputeVolume(common), Precision);
        Assert.Equal(27000 - 1000, _kernel.ComputeVolume(hollow), Precision);
        Assert.Equal(12, _kernel.Tessellate(hollow, 1).FaceCount); // the outer box and the cavity
    }

    [Fact]
    public void Boxes_with_coplanar_faces_unite_with_their_faces_merged()
    {
        using IKernelShape first = _kernel.MakeBox(10, 10, 10);
        using IKernelShape second = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(5, 0, 0));

        using IKernelShape union = _kernel.Union(first, second);

        Assert.Equal(1500, _kernel.ComputeVolume(union), Precision);
        Assert.Equal(6, _kernel.Tessellate(union, 1).FaceCount);
    }

    [Fact]
    public void A_detail_a_hundred_million_times_smaller_than_the_shape_is_cut_out()
    {
        using IKernelShape large = _kernel.MakeBox(1e5, 1e5, 1e5);
        using IKernelShape small = _kernel.MakeBox(1e-3, 1e-3, 1e-3);

        using IKernelShape notched = _kernel.Subtract(large, small);

        Assert.Equal(9, _kernel.Tessellate(notched, 100).FaceCount); // three faces of the notch, six of the box
    }

    [Fact]
    public void Curved_shapes_combine_into_the_volumes_of_their_formulas()
    {
        using IKernelShape sphere = _kernel.MakeSphere(10);
        using IKernelShape upperHalf = _kernel.Place(_kernel.MakeBox(20, 20, 10), Transform.Translation(-10, -10, 0));
        using IKernelShape cylinder = _kernel.MakeCylinder(10, 10);
        using IKernelShape tangentBox = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(10, -5, 0));

        using IKernelShape hemisphere = _kernel.Intersect(sphere, upperHalf);
        using IKernelShape tangent = _kernel.Union(cylinder, tangentBox);

        Assert.Equal(2.0 / 3.0 * Math.PI * 1000, _kernel.ComputeVolume(hemisphere), 1e-3);
        Assert.Equal(Math.PI * 100 * 10 + 1000, _kernel.ComputeVolume(tangent), 1e-3);
    }

    [Fact]
    public void Results_combine_again_and_survive_a_write_and_a_read()
    {
        using IKernelShape box = _kernel.MakeBox(30, 30, 10);
        using IKernelShape hole = _kernel.Place(_kernel.MakeCylinder(5, 10), Transform.Translation(15, 15, 0));
        using IKernelShape boss = _kernel.Place(_kernel.MakeCylinder(5, 10), Transform.Translation(15, 15, 10));

        using IKernelShape drilled = _kernel.Subtract(box, hole);
        using IKernelShape read = _kernel.Deserialize(_kernel.Serialize(drilled));
        using IKernelShape result = _kernel.Union(read, boss);

        Assert.Equal(9000 - Math.PI * 250 + Math.PI * 250, _kernel.ComputeVolume(result), 1e-3);
        AssertBox(new Point3d(0, 0, 0), new Point3d(30, 30, 20), _kernel.GetBoundingBox(result));
    }

    private static void AssertBox(Point3d min, Point3d max, BoundingBox box)
    {
        Assert.Equal(min.X, box.Min.X, Precision);
        Assert.Equal(min.Y, box.Min.Y, Precision);
        Assert.Equal(min.Z, box.Min.Z, Precision);
        Assert.Equal(max.X, box.Max.X, Precision);
        Assert.Equal(max.Y, box.Max.Y, Precision);
        Assert.Equal(max.Z, box.Max.Z, Precision);
    }
}
