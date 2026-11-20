using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class AttachmentTests
{
    private const double Tolerance = 1e-9;

    // A table with a leaf at its end, holding an anchor at the far edge of the leaf; a chair beside it.
    private readonly Document _document = new();
    private readonly BlockReference _table;
    private readonly BlockReference _leaf;
    private readonly AnchorEntity _anchor;
    private readonly BlockReference _chair;

    public AttachmentTests()
    {
        var leafBlock = new Block("Leaf");
        _anchor = new AnchorEntity(Transform.Translation(450, 0, 0)) { Name = "End" };
        leafBlock.Entities.Add(_anchor);
        var tableBlock = new Block("Table");
        _leaf = new BlockReference(leafBlock, Transform.Translation(900, 0, 0));
        tableBlock.Entities.Add(_leaf);
        var chairBlock = new Block("Chair");
        _document.Blocks.AddRange([leafBlock, tableBlock, chairBlock]);
        _table = new BlockReference(tableBlock, Transform.Identity);
        _chair = new BlockReference(chairBlock, Transform.Translation(1500, 0, 0));
        _document.Root.Entities.Add(_table);
        _document.Root.Entities.Add(_chair);
    }

    private Attachment Attach()
    {
        var attachment = new Attachment(_chair, [_table, _leaf], _anchor,
            Attachment.OffsetOf(_chair, [_table, _leaf], _anchor));
        _document.Attach(attachment);
        return attachment;
    }

    [Fact]
    public void An_anchor_placed_again_takes_its_follower_along_and_undoing_brings_both_back()
    {
        Attach();
        var command = new Core.Editing.SetAnchorCommand("Move anchor", _anchor, Transform.Translation(450, 300, 0));

        command.Apply();
        Assert.Equal(new Point3d(1500, 300, 0), Origin(_chair));

        command.Revert();
        Assert.Equal(new Point3d(1500, 0, 0), Origin(_chair));
        Assert.Equal(new Point3d(450, 0, 0), _anchor.Point);
    }

    [Fact]
    public void Moving_an_anchor_by_the_transform_commands_keeps_it_the_one_followed()
    {
        Attachment attachment = Attach();

        Core.Editing.TransformCommands.Of("Move", _anchor, Transform.Translation(0, 0, 100)).Apply();

        Assert.Same(_anchor, attachment.Anchor);
        Assert.Contains(_anchor, _leaf.Block.Entities);
        Assert.Equal(new Point3d(1500, 0, 100), Origin(_chair));
    }

    [Fact]
    public void The_follower_goes_with_the_references_of_the_chain_and_keeps_its_place_when_moved_alone()
    {
        Attachment attachment = Attach();
        Assert.Equal(new Point3d(1500, 0, 0), Origin(_chair));

        _table.Transform = Transform.Translation(0, 1000, 0);
        Assert.Equal(new Point3d(1500, 1000, 0), Origin(_chair));

        _leaf.Transform = Transform.Translation(1350, 0, 0); // the leaf slid out
        Assert.Equal(new Point3d(1950, 1000, 0), Origin(_chair));

        _chair.Transform = Transform.Translation(2000, 1000, 0); // pulled back a little
        _table.Transform = Transform.Identity;
        Assert.Equal(new Point3d(2000, 0, 0), Origin(_chair));

        Assert.True(_document.Detach(attachment));
        _table.Transform = Transform.Translation(0, 1000, 0);
        Assert.Equal(new Point3d(2000, 0, 0), Origin(_chair));
    }

    [Fact]
    public void An_anchor_in_a_part_scaled_to_nothing_places_the_follower_at_the_origin_of_the_part_unscaled()
    {
        _leaf.Transform = Transform.Translation(900, 0, 0) * Transform.Scaling(Point3d.Origin, 1e-6);
        Attachment attachment = Attach();

        _leaf.Transform = Transform.Translation(900, 0, 0); // grown to its size
        Assert.Equal(1950, Origin(_chair).X, 1e-3);
        Assert.Equal(1, _chair.Transform.ApplyVector(Vector3d.UnitX).Length, Tolerance);
        Assert.True(attachment.IsActive);
    }

    [Fact]
    public void A_reference_follows_one_anchor_and_never_one_that_moves_with_it()
    {
        Attach();
        Assert.Throws<ArgumentException>(() => Attach());

        // The table cannot follow an anchor of the chair, which follows the table.
        var chairAnchor = new AnchorEntity(Transform.Identity);
        _chair.Block.Entities.Add(chairAnchor);
        var loop = new Attachment(_table, [_chair], chairAnchor, Transform.Identity);
        Assert.Throws<ArgumentException>(() => _document.Attach(loop));

        // Nor a chain that does not reach the anchor, or goes through the follower.
        Assert.Throws<ArgumentException>(() => new Attachment(_chair, [_table], _anchor, Transform.Identity));
        Assert.Throws<ArgumentException>(() => new Attachment(_chair, [_chair], chairAnchor, Transform.Identity));
    }

    [Fact]
    public void Removing_the_follower_or_a_reference_of_the_chain_stops_the_attachment_until_it_comes_back()
    {
        Attachment attachment = Attach();
        _document.Root.Entities.Remove(_chair);
        Assert.False(attachment.IsActive);
        _table.Transform = Transform.Translation(0, 500, 0);

        _document.Root.Entities.Add(_chair);
        Assert.True(attachment.IsActive);
        _table.Transform = Transform.Translation(0, 700, 0);
        Assert.Equal(new Point3d(1500, 700, 0), Origin(_chair));
    }

    private static Point3d Origin(BlockReference reference) => reference.Transform.Apply(Point3d.Origin);
}
