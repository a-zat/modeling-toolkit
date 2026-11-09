using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class ImportCommandTests
{
    [Fact]
    public void A_model_imported_becomes_a_block_placed_at_the_origin_with_its_blocks_and_materials_until_undone()
    {
        var document = new Document();
        document.Blocks.Add(new Block("Chair"));
        var session = new EditSession(document);

        var imported = new Document();
        var leg = new Block("Leg");
        imported.Blocks.Add(leg);
        var oak = new Material("Oak");
        imported.Materials.Add(oak);
        var seat = new BrepEntity(new NoKernel(), new NoShape());
        imported.Root.Entities.Add(seat);
        imported.Root.Entities.Add(new BlockReference(leg, Transform.Translation(10, 0, 0)));

        var import = new ImportCommand(document, document.Root, imported, "Chair");
        session.Execute(import);

        BlockReference placed = Assert.IsType<BlockReference>(Assert.Single(document.Root.Entities));
        Assert.Same(import.Reference, placed);
        Assert.Equal("Chair 2", placed.Block.Name);
        Assert.Equal(Transform.Identity, placed.Transform);
        Assert.Same(seat, placed.Block.Entities[0]);
        Assert.Same(placed.Block, seat.Owner);
        Assert.Equal(["Chair", "Leg", "Chair 2"], document.Blocks.Select(block => block.Name));
        Assert.Equal([oak], document.Materials);
        Assert.Empty(imported.Root.Entities);
        Assert.Equal("Import", session.UndoName);

        session.Undo();
        Assert.Empty(document.Root.Entities);
        Assert.Equal(["Chair"], document.Blocks.Select(block => block.Name));
        Assert.Empty(document.Materials);
        session.Redo();
        Assert.Same(placed, Assert.Single(document.Root.Entities));
    }

    [Fact]
    public void An_empty_model_or_a_block_of_another_document_is_refused()
    {
        var document = new Document();
        Assert.Throws<ArgumentException>(() => new ImportCommand(document, document.Root, new Document(), "Empty"));
        var other = new Document();
        var full = new Document();
        full.Root.Entities.Add(new BrepEntity(new NoKernel(), new NoShape()));
        Assert.Throws<ArgumentException>(() => new ImportCommand(document, other.Root, full, "Part"));
    }
}
