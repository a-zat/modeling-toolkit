using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class LayerTests
{
    private readonly Document _document = new();
    private readonly Layer _living = new("Living room");
    private readonly BlockReference _table;
    private readonly BlockReference _chair;
    private readonly List<DocumentChange> _changes = [];

    public LayerTests()
    {
        var chair = new Block("Chair");
        var table = new Block("Table");
        _chair = new BlockReference(chair, Transform.Identity);
        table.Entities.Add(_chair);
        _document.Blocks.AddRange([chair, table]);
        _table = new BlockReference(table, Transform.Identity);
        _document.Root.Entities.Add(_table);
        _document.Changed += (_, change) => _changes.Add(change);
    }

    [Fact]
    public void An_entity_on_no_layer_is_on_that_of_the_nearest_reference_above_it()
    {
        Assert.Null(Layer.Of([_table, _chair]));

        _table.Layer = _living;
        Assert.Same(_living, Layer.Of([_table, _chair]));

        var kitchen = new Layer("Kitchen");
        _chair.Layer = kitchen;
        Assert.Same(kitchen, Layer.Of([_table, _chair]));
        Assert.Contains(new PropertyChanged(_chair, nameof(Entity.Layer)), _changes);
    }

    [Fact]
    public void A_layer_listed_raises_its_changes_through_the_document()
    {
        new AddLayerCommand(_document, _living).Apply();
        new SetLayerHiddenCommand(_living, true).Apply();
        new RenameLayerCommand(_living, "Lounge").Apply();

        Assert.Equal([_living], _document.Layers);
        Assert.Equal(
        [
            new LayersChanged(_living, Added: true),
            new PropertyChanged(_living, nameof(Layer.IsHidden)),
            new PropertyChanged(_living, nameof(Layer.Name)),
        ], _changes);
        Assert.Throws<ArgumentException>(() => new Document().AddLayer(_living));
    }

    [Fact]
    public void Deleting_a_layer_leaves_its_entities_on_none_and_undoing_puts_them_back()
    {
        var kitchen = new Layer("Kitchen");
        _document.AddLayer(kitchen);
        _document.AddLayer(_living);
        var set = new SetLayerCommand([_table, _chair], _living);
        set.Apply();
        var delete = new DeleteLayerCommand(_document, _living);

        delete.Apply();
        Assert.Equal([kitchen], _document.Layers);
        Assert.Null(_table.Layer);
        Assert.Null(_chair.Layer);

        delete.Revert();
        Assert.Equal([kitchen, _living], _document.Layers);
        Assert.Same(_living, _chair.Layer);
        set.Revert();
        Assert.Null(_table.Layer);
    }

    [Fact]
    public void A_duplicate_is_on_the_layer_of_the_original()
    {
        _table.Layer = _living;

        Assert.Same(_living, TransformCommands.Duplicate(_table, Transform.Identity).Layer);
    }
}
