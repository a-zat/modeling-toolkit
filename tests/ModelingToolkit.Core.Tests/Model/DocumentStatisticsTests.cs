using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class DocumentStatisticsTests
{
    [Fact]
    public void Shared_blocks_are_counted_once_and_placed_many_times()
    {
        var wheel = new Block("Wheel");
        wheel.Entities.Add(new MeshEntity(
            [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)],
            [new Triangle(0, 1, 2), new Triangle(1, 3, 2)]));

        var axle = new Block("Axle");
        axle.Entities.Add(new BlockReference(wheel, Transform.Translation(-10, 0, 0)));
        axle.Entities.Add(new BlockReference(wheel, Transform.Translation(10, 0, 0)));

        var document = new Document();
        document.Blocks.AddRange([wheel, axle, new Block("Unused")]);
        document.Materials.Add(new Material("Rubber"));
        document.Root.Entities.Add(new BlockReference(axle, Transform.Translation(0, -20, 0)));
        document.Root.Entities.Add(new BlockReference(axle, Transform.Translation(0, 20, 0)));

        DocumentStatistics statistics = DocumentStatistics.Of(document);

        Assert.Equal(2, statistics.Blocks);
        Assert.Equal(4, statistics.BlockReferences);
        Assert.Equal((1, 4L, 2L), (statistics.Meshes, statistics.Vertices, statistics.Triangles));
        Assert.Equal((4L, 8L), (statistics.PlacedMeshes, statistics.PlacedTriangles));
        Assert.Equal((1, 0), (statistics.Materials, statistics.Textures));
        Assert.Equal(new BoundingBox(new Point3d(-10, -20, 0), new Point3d(11, 21, 0)), statistics.BoundingBox);
    }

    [Fact]
    public void Bounds_are_those_of_the_placed_vertices_even_through_rotations()
    {
        var block = new Block("Diagonal");
        block.Entities.Add(new MeshEntity([new(0, 0, 0), new(1, 1, 0), new(1, 1, 1)], [new Triangle(0, 1, 2)]));
        double c = Math.Sqrt(0.5);
        var rotation45 = new Transform(
            c, -c, 0, 0,
            c, c, 0, 0,
            0, 0, 1, 0);
        var document = new Document();
        document.Blocks.Add(block);
        document.Root.Entities.Add(new BlockReference(block, rotation45));

        BoundingBox exact = DocumentStatistics.Of(document).BoundingBox;

        // The diagonal is turned onto the Y axis: the box is a segment, while the rotated box of the block is a square.
        Assert.Equal(0, exact.Min.X, 1e-12);
        Assert.Equal(0, exact.Max.X, 1e-12);
        Assert.Equal(Math.Sqrt(2), exact.Max.Y, 1e-12);
        Assert.True(document.GetBoundingBox().Min.X < -0.5);
    }
}
