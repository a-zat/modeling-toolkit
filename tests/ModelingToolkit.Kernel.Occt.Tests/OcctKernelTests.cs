using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class OcctKernelTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void Info_reports_the_OpenCASCADE_version()
    {
        Assert.Equal("OpenCASCADE", _kernel.Info.Name);
        Assert.Matches(@"^\d+\.\d+\.\d+", _kernel.Info.Version);
    }

    [Fact]
    public void Box_volume_is_the_product_of_its_dimensions()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);

        Assert.Equal(6000, _kernel.ComputeVolume(box), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MakeBox_rejects_invalid_dimensions_with_the_native_error_message(double size)
    {
        var exception = Assert.Throws<ArgumentException>(() => _kernel.MakeBox(size, 1, 1));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void Primitives_have_the_volumes_of_their_formulas()
    {
        using IKernelShape cylinder = _kernel.MakeCylinder(10, 40);
        using IKernelShape sphere = _kernel.MakeSphere(25);
        using IKernelShape cone = _kernel.MakeCone(30, 10, 60);
        using IKernelShape torus = _kernel.MakeTorus(50, 10);

        Assert.Equal(Math.PI * 10 * 10 * 40, _kernel.ComputeVolume(cylinder), precision: 3);
        Assert.Equal(4.0 / 3 * Math.PI * 25 * 25 * 25, _kernel.ComputeVolume(sphere), precision: 3);
        Assert.Equal(Math.PI * 60 / 3 * (30 * 30 + 30 * 10 + 10 * 10), _kernel.ComputeVolume(cone), precision: 3);
        Assert.Equal(2 * Math.PI * Math.PI * 50 * 10 * 10, _kernel.ComputeVolume(torus), precision: 3);
    }

    [Fact]
    public void Primitives_stand_where_the_kernel_documents()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);
        using IKernelShape cylinder = _kernel.MakeCylinder(10, 40);
        using IKernelShape sphere = _kernel.MakeSphere(25);
        using IKernelShape cone = _kernel.MakeCone(30, 10, 60);
        using IKernelShape torus = _kernel.MakeTorus(50, 10);

        AssertBox(new Point3d(0, 0, 0), new Point3d(10, 20, 30), _kernel.GetBoundingBox(box));
        AssertBox(new Point3d(-10, -10, 0), new Point3d(10, 10, 40), _kernel.GetBoundingBox(cylinder));
        AssertBox(new Point3d(-25, -25, -25), new Point3d(25, 25, 25), _kernel.GetBoundingBox(sphere));
        AssertBox(new Point3d(-30, -30, 0), new Point3d(30, 30, 60), _kernel.GetBoundingBox(cone));
        AssertBox(new Point3d(-60, -60, -10), new Point3d(60, 60, 10), _kernel.GetBoundingBox(torus));
    }

    [Fact]
    public void A_placed_shape_is_moved_and_the_original_is_not()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);
        // A quarter turn about Z, then a translation: (x, y, z) -> (-y + 100, x + 200, z + 300).
        var placement = new Transform(
            0, -1, 0, 100,
            1, 0, 0, 200,
            0, 0, 1, 300);

        using IKernelShape placed = _kernel.Place(box, placement);

        AssertBox(new Point3d(80, 200, 300), new Point3d(100, 210, 330), _kernel.GetBoundingBox(placed));
        Assert.Equal(6000, _kernel.ComputeVolume(placed), precision: 6);
        AssertBox(new Point3d(0, 0, 0), new Point3d(10, 20, 30), _kernel.GetBoundingBox(box));
    }

    [Theory]
    [InlineData(2, 0, 0, 0, 2, 0, 0, 0, 2)] // scaling
    [InlineData(1, 0, 0, 0, 1, 0, 0, 0, -1)] // reflection
    [InlineData(1, 1, 0, 0, 1, 0, 0, 0, 1)] // shearing
    public void Placements_that_are_not_rigid_are_rejected(
        double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
    {
        using IKernelShape box = _kernel.MakeBox(1, 1, 1);

        var exception = Assert.Throws<ArgumentException>(
            () => _kernel.Place(box, new Transform(m00, m01, m02, 0, m10, m11, m12, 0, m20, m21, m22, 0)));

        Assert.Contains("rotation", exception.Message);
    }

    [Fact]
    public void Primitives_reject_invalid_dimensions_with_the_native_error_message()
    {
        Assert.Contains("Cylinder", Assert.Throws<ArgumentException>(() => _kernel.MakeCylinder(0, 1)).Message);
        Assert.Contains("Sphere", Assert.Throws<ArgumentException>(() => _kernel.MakeSphere(-1)).Message);
        Assert.Contains("cylinder", Assert.Throws<ArgumentException>(() => _kernel.MakeCone(2, 2, 1)).Message);
        Assert.Contains("minor", Assert.Throws<ArgumentException>(() => _kernel.MakeTorus(2, 3)).Message);
    }

    [Fact]
    public void A_disposed_shape_cannot_be_used()
    {
        IKernelShape box = _kernel.MakeBox(1, 1, 1);
        box.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _kernel.ComputeVolume(box));
        Assert.Throws<ObjectDisposedException>(() => _kernel.GetBoundingBox(box));
        Assert.Throws<ObjectDisposedException>(() => _kernel.Place(box, Transform.Identity));
    }

    private static void AssertBox(Point3d min, Point3d max, BoundingBox actual)
    {
        const double tolerance = 1e-6;
        Assert.Equal(min.X, actual.Min.X, tolerance);
        Assert.Equal(min.Y, actual.Min.Y, tolerance);
        Assert.Equal(min.Z, actual.Min.Z, tolerance);
        Assert.Equal(max.X, actual.Max.X, tolerance);
        Assert.Equal(max.Y, actual.Max.Y, tolerance);
        Assert.Equal(max.Z, actual.Max.Z, tolerance);
    }
}
