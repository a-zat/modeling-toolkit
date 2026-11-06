using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class BrepEntityTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void A_shape_has_the_bounding_box_the_kernel_computes()
    {
        var entity = new BrepEntity(_kernel, _kernel.MakeCylinder(10, 40));

        BoundingBox box = entity.GetBoundingBox();

        Assert.Equal(-10, box.Min.X, tolerance: 1e-6);
        Assert.Equal(-10, box.Min.Y, tolerance: 1e-6);
        Assert.Equal(0, box.Min.Z, tolerance: 1e-6);
        Assert.Equal(10, box.Max.X, tolerance: 1e-6);
        Assert.Equal(10, box.Max.Y, tolerance: 1e-6);
        Assert.Equal(40, box.Max.Z, tolerance: 1e-6);
    }

    [Fact]
    public void Shapes_count_in_the_statistics_and_the_bounds_of_a_document_as_placed()
    {
        var pin = new Block("Pin") { Entities = { new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)) } };
        var document = new Document();
        document.Blocks.Add(pin);
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100)));
        document.Root.Entities.Add(new BlockReference(pin, Transform.Translation(500, 0, 0)));
        document.Root.Entities.Add(new BlockReference(pin, Transform.Translation(0, 700, 0)));

        DocumentStatistics statistics = DocumentStatistics.Of(document);

        Assert.Equal(2, statistics.BrepShapes);
        Assert.Equal(3, statistics.PlacedBrepShapes);
        Assert.Equal(0, statistics.Meshes);
        Assert.Equal(1, statistics.Blocks);
        Assert.Equal(new Point3d(0, 0, 0), statistics.BoundingBox.Min);
        Assert.Equal(new Point3d(510, 710, 100), statistics.BoundingBox.Max);
        Assert.Equal(statistics.BoundingBox.Max.X, document.GetBoundingBox().Max.X, tolerance: 1e-6);
    }

    [Fact]
    public void A_shape_needs_a_kernel_and_a_shape()
    {
        using var box = _kernel.MakeBox(1, 1, 1);

        Assert.Throws<ArgumentNullException>(() => new BrepEntity(null!, box));
        Assert.Throws<ArgumentNullException>(() => new BrepEntity(_kernel, null!));
    }
}
