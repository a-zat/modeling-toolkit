using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class DocumentChangeTests
{
    private static MeshEntity Triangle() =>
        new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new Triangle(0, 1, 2)]);

    private static List<DocumentChange> Record(Document document)
    {
        var changes = new List<DocumentChange>();
        document.Changed += (_, change) => changes.Add(change);
        return changes;
    }

    [Fact]
    public void Adding_removing_and_replacing_entities_are_raised_with_their_index()
    {
        var document = new Document();
        List<DocumentChange> changes = Record(document);
        MeshEntity first = Triangle(), second = Triangle(), third = Triangle();

        document.Root.Entities.Add(first);
        document.Root.Entities.Add(second);
        document.Root.Entities[1] = third;
        document.Root.Entities.RemoveAt(0);

        Assert.Equal<DocumentChange>(
        [
            new EntityAdded(document.Root, 0, first),
            new EntityAdded(document.Root, 1, second),
            new EntityReplaced(document.Root, 1, second, third),
            new EntityRemoved(document.Root, 0, first),
        ], changes);
        Assert.Null(first.Owner);
        Assert.Null(second.Owner);
        Assert.Same(document.Root, third.Owner);
    }

    [Fact]
    public void Clearing_a_block_removes_its_entities_from_the_last()
    {
        var document = new Document();
        MeshEntity first = Triangle(), second = Triangle();
        document.Root.Entities.Add(first);
        document.Root.Entities.Add(second);
        List<DocumentChange> changes = Record(document);

        document.Root.Entities.Clear();

        Assert.Equal<DocumentChange>(
            [new EntityRemoved(document.Root, 1, second), new EntityRemoved(document.Root, 0, first)], changes);
    }

    [Fact]
    public void Changes_inside_listed_blocks_are_raised()
    {
        var document = new Document();
        var block = new Block("Leg");
        List<DocumentChange> changes = Record(document);

        document.Blocks.Add(block);
        MeshEntity mesh = Triangle();
        block.Entities.Add(mesh);
        block.Name = "Table leg";
        document.Blocks.Remove(block);
        block.Name = "Unlisted";

        Assert.Equal<DocumentChange>(
        [
            new BlockAdded(block),
            new EntityAdded(block, 0, mesh),
            new PropertyChanged(block, nameof(Block.Name)),
            new BlockRemoved(block),
        ], changes);
        Assert.Null(block.Document);
    }

    [Fact]
    public void Properties_of_entities_raise_a_change_only_when_they_change()
    {
        var document = new Document();
        var block = new Block("Leg");
        document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        document.Root.Entities.Add(reference);
        var material = new Material("Oak");
        List<DocumentChange> changes = Record(document);

        reference.Name = "Front leg";
        reference.Name = "Front leg";
        reference.Transform = Transform.Translation(10, 0, 0);

        Assert.Equal<DocumentChange>(
        [
            new PropertyChanged(reference, nameof(Entity.Name)),
            new PropertyChanged(reference, nameof(BlockReference.Transform)),
        ], changes);
    }

    [Fact]
    public void An_entity_belongs_to_one_block_at_most()
    {
        var document = new Document();
        var block = new Block("Other");
        MeshEntity mesh = Triangle();
        document.Root.Entities.Add(mesh);

        Assert.Throws<InvalidOperationException>(() => block.Entities.Add(mesh));
        Assert.Throws<InvalidOperationException>(() => document.Root.Entities.Add(mesh));
    }

    [Fact]
    public void A_block_belongs_to_one_document_at_most()
    {
        var block = new Block("Leg");
        new Document().Blocks.Add(block);

        Assert.Throws<InvalidOperationException>(() => new Document().Blocks.Add(block));
    }

    [Fact]
    public void An_entity_outside_a_document_raises_nothing()
    {
        var block = new Block("Loose");
        MeshEntity mesh = Triangle();
        block.Entities.Add(mesh);

        mesh.Name = "Renamed"; // no document to raise it: nothing happens

        Assert.Same(block, mesh.Owner);
        Assert.Null(block.Document);
    }
}
