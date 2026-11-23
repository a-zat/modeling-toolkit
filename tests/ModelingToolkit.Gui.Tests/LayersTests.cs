using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

public sealed class LayersTests
{
    private readonly Document _document = new();
    private readonly EditSession _session;
    private readonly Layer _living = new("Living room");
    private readonly BlockReference _table;
    private readonly BlockReference _chair;
    private readonly MeshEntity _floor = Square();

    public LayersTests()
    {
        var chair = new Block("Chair") { Entities = { Square() } };
        var table = new Block("Table") { Entities = { Square() } };
        _chair = new BlockReference(chair, Transform.Identity) { Name = "Chair" };
        table.Entities.Add(_chair);
        _document.Blocks.AddRange([chair, table]);
        _table = new BlockReference(table, Transform.Identity) { Name = "Table" };
        _document.Root.Entities.Add(_table);
        _document.Root.Entities.Add(_floor);
        _document.AddLayer(_living);
        _session = new EditSession(_document);
    }

    private static MeshEntity Square() =>
        new([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)], [new(0, 1, 2), new(0, 2, 3)]);

    private bool Edit(IEditCommand command)
    {
        _session.Execute(command);
        return true;
    }

    [Fact]
    public void The_outline_by_layer_lists_what_is_on_each_layer_and_then_the_model_space_on_none()
    {
        var layers = new LayersViewModel { Edit = Edit, IsShown = true };
        layers.Show(_document);
        _session.Execute(new SetLayerCommand([_table], _living));

        Assert.Equal(["Living room", "No layer"], layers.Rows.Select(row => row.Name));
        Assert.Equal(["Table"], layers.Rows[0].Items.Select(item => item.Name)); // the chair is on it from above
        Assert.Equal(["Mesh"], layers.Rows[1].Items.Select(item => item.Name));

        _session.Execute(new SetLayerCommand([_chair], _living));
        LayerItemViewModel chair = layers.Rows[0].Items[1];
        Assert.Equal(("Chair", "Table"), (chair.Name, chair.Where));
        IReadOnlyList<Entity>? selected = null;
        layers.Select = path => selected = path;
        layers.SelectedItem = chair;
        Assert.Equal([_table, _chair], selected);

        layers.ToggleVisibleCommand.Execute(layers.Rows[0]);
        Assert.True(_living.IsHidden);
        Assert.False(layers.Rows[0].IsVisible);
        layers.DeleteCommand.Execute(layers.Rows[0]);
        Assert.Equal(["No layer"], layers.Rows.Select(row => row.Name));
        _session.Undo();
        Assert.Same(_living, _chair.Layer);
    }

    [Fact]
    public void A_layer_added_is_named_apart_and_renamed_from_its_row()
    {
        var layers = new LayersViewModel { Edit = Edit, IsShown = true };
        layers.Show(_document);

        layers.AddLayerCommand.Execute(null);
        LayerRowViewModel added = layers.Rows[1];
        Assert.Equal("Layer 1", added.Name);
        Assert.True(added.IsRenaming);
        added.EditedName = "  Kitchen ";
        layers.CommitRenameCommand.Execute(added);

        Assert.Equal(["Living room", "Kitchen"], _document.Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void The_properties_put_the_entities_selected_on_a_layer_and_show_the_one_from_above()
    {
        var tree = new ModelTreeViewModel { Edit = Edit };
        tree.Show(_document);
        var properties = new PropertiesViewModel { Edit = Edit };
        properties.Know(_document, DocumentStatistics.Of(_document));
        ModelNodeViewModel table = tree.Model!.Children[0];
        properties.SelectedEntities = () => [_table, _floor];
        properties.Show(table);

        Assert.Equal("None", properties.LayerName);
        Assert.Equal(["None", "Living room", "New layer"], properties.LayerChoices.Select(choice => choice.Label));
        properties.ChooseLayerCommand.Execute(properties.LayerChoices[1]);
        Assert.Same(_living, _table.Layer);
        Assert.Same(_living, _floor.Layer);

        properties.SelectedEntities = null;
        properties.Show(table.Children.Single(node => node.Entity == _chair));
        Assert.Equal("Living room (from above)", properties.LayerName);
        properties.ChooseLayerCommand.Execute(properties.LayerChoices[^1]); // a new layer
        Assert.Equal("Layer 1", _chair.Layer?.Name);
        Assert.Equal(2, _document.Layers.Count);
        _session.Undo();
        Assert.Single(_document.Layers);
        Assert.Null(_chair.Layer);
    }

    [Fact]
    public void The_3D_view_leaves_out_what_is_on_a_hidden_layer_with_what_it_places()
    {
        _table.Layer = _living;
        int all = ViewportScene.Of(_document).Instances.Count;

        _living.IsHidden = true;

        Assert.Equal(3, all); // the table, its chair and the floor
        Assert.Single(ViewportScene.Of(_document).Instances);
    }
}
