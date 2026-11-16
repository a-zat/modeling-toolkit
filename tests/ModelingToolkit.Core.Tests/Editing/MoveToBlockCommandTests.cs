using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class MoveToBlockCommandTests
{
    [Fact]
    public void A_reference_moved_into_a_block_stays_where_it_was_and_undoing_puts_it_back()
    {
        // A sofa at (5000, 1000) in the project; a living room placed at (3000, 0), turned a quarter.
        var document = new Document();
        var sofa = new BlockReference(new Block("Sofa"), Transform.Translation(5000, 1000, 0));
        var room = new Block("Living room");
        Transform roomPlacement = Transform.Translation(3000, 0, 0) *
            Transform.Rotation(default, Vector3d.UnitZ, Math.PI / 2);
        document.Root.Entities.Add(new BlockReference(room, roomPlacement));
        document.Root.Entities.Add(sofa);
        var session = new EditSession(document);
        Point3d before = sofa.Transform.Apply(default);

        session.Execute(new MoveToBlockCommand(sofa, Transform.Identity, room, roomPlacement));

        Assert.Same(room, sofa.Owner);
        Assert.DoesNotContain(sofa, document.Root.Entities);
        Point3d after = (roomPlacement * sofa.Transform).Apply(default);
        Assert.Equal(before.X, after.X, 1e-6);
        Assert.Equal(before.Y, after.Y, 1e-6);

        session.Undo();
        Assert.Same(document.Root, sofa.Owner);
        Assert.Equal(1, document.Root.Entities.IndexOf(sofa));
        Assert.Equal(before, sofa.Transform.Apply(default));
    }

    [Fact]
    public void A_block_cannot_go_into_itself_nor_into_what_it_places()
    {
        var document = new Document();
        var inner = new Block("Inner");
        var outer = new Block("Outer") { Entities = { new BlockReference(inner, Transform.Identity) } };
        var reference = new BlockReference(outer, Transform.Identity);
        document.Root.Entities.Add(reference);

        Assert.Throws<ArgumentException>(() =>
            new MoveToBlockCommand(reference, Transform.Identity, inner, Transform.Identity));
        Assert.Throws<ArgumentException>(() =>
            new MoveToBlockCommand(reference, Transform.Identity, outer, Transform.Identity));
        Assert.Throws<ArgumentException>(() =>
            new MoveToBlockCommand(reference, Transform.Identity, document.Root, Transform.Identity));
    }
}
