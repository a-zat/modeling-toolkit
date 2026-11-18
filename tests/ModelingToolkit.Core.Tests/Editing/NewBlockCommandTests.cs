using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class NewBlockCommandTests
{
    [Fact]
    public void A_new_block_is_placed_in_another_by_a_reference_at_its_origin_with_a_name_of_its_own()
    {
        var document = new Document();
        var zone = new Block("Zone");
        document.Blocks.Add(zone);
        document.Blocks.Add(new Block("Room"));
        var session = new EditSession(document);

        var command = new NewBlockCommand(document, zone, "Room");
        session.Execute(command);

        Assert.Equal("Room 2", command.Reference.Block.Name);
        Assert.Same(command.Reference, Assert.Single(zone.Entities));
        Assert.Equal(Transform.Identity, command.Reference.Transform);
        Assert.Contains(command.Reference.Block, document.Blocks);

        session.Undo();
        Assert.Empty(zone.Entities);
        Assert.DoesNotContain(command.Reference.Block, document.Blocks);
    }

    [Fact]
    public void Renaming_the_only_reference_of_a_block_renames_the_block_and_the_others_keep_theirs()
    {
        var document = new Document();
        var session = new EditSession(document);
        var added = new NewBlockCommand(document, document.Root, "New block");
        session.Execute(added);
        var chair = new Block("Chair");
        document.Blocks.Add(chair);
        var first = new BlockReference(chair, Transform.Identity);
        document.Root.Entities.Add(first);
        document.Root.Entities.Add(new BlockReference(chair, Transform.Translation(500, 0, 0)));

        session.Execute(new RenameCommand(added.Reference, "Living room", document));
        session.Execute(new RenameCommand(first, "Chair by the window", document));

        Assert.Equal("Living room", added.Reference.Block.Name);
        Assert.Equal("Chair", chair.Name); // placed twice
        session.Undo();
        session.Undo();
        Assert.Equal("New block", added.Reference.Block.Name);
    }
}
