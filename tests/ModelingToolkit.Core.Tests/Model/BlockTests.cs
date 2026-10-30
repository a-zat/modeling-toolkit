using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class BlockTests
{
    // A triangle whose bounding box is the unit cube.
    private static MeshEntity UnitTriangle() =>
        new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 1)], [new Triangle(0, 1, 2)]);

    [Fact]
    public void Bounding_box_follows_nested_references()
    {
        var unit = new Block("Unit");
        unit.Entities.Add(UnitTriangle());

        var pair = new Block("Pair");
        pair.Entities.Add(new BlockReference(unit, Transform.Identity));
        pair.Entities.Add(new BlockReference(unit, Transform.Translation(10, 0, 0)));

        var document = new Document();
        document.Blocks.AddRange([unit, pair]);
        document.Root.Entities.Add(new BlockReference(pair, Transform.Translation(0, 5, 0)));
        document.Root.Entities.Add(new BlockReference(pair, Transform.Translation(0, -5, 0)));

        Assert.Equal(new BoundingBox(new Point3d(0, -5, 0), new Point3d(11, 6, 1)), document.GetBoundingBox());
    }

    [Fact]
    public void Empty_document_has_an_empty_bounding_box()
    {
        Assert.True(new Document().GetBoundingBox().IsEmpty);
    }

    [Fact]
    public void A_block_cannot_reference_itself()
    {
        var block = new Block("A");

        Assert.Throws<InvalidOperationException>(() => block.Entities.Add(new BlockReference(block, Transform.Identity)));
    }

    [Fact]
    public void A_block_cannot_reference_itself_through_other_blocks()
    {
        var a = new Block("A");
        var b = new Block("B");
        a.Entities.Add(new BlockReference(b, Transform.Identity));
        b.Entities.Add(UnitTriangle());

        Assert.Throws<InvalidOperationException>(() => b.Entities.Add(new BlockReference(a, Transform.Identity)));
        Assert.Throws<InvalidOperationException>(() => b.Entities[0] = new BlockReference(a, Transform.Identity));
    }

    [Fact]
    public void Entities_cannot_be_null()
    {
        Assert.Throws<ArgumentNullException>(() => new Block("A").Entities.Add(null!));
    }
}
