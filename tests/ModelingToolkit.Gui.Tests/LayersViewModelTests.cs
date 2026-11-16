using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Tests;

public sealed class LayersViewModelTests
{
    [Fact]
    public void Layers_are_the_entities_of_the_model_space_hidden_by_commands_that_can_be_undone()
    {
        var document = new Document();
        var walls = new BlockReference(new Block("Walls"), Transform.Identity) { Name = "Walls" };
        var floors = new BlockReference(new Block("Floors"), Transform.Identity);
        document.Root.Entities.Add(walls);
        document.Root.Entities.Add(floors);
        var session = new EditSession(document);
        var layers = new LayersViewModel { Edit = command => { session.Execute(command); return true; } };
        layers.Show(document);

        Assert.Equal(["Walls", "Floors"], layers.Items.Select(item => item.Name));
        layers.Items[0].IsVisible = false;
        Assert.True(walls.IsHidden);

        session.Undo();
        layers.Refresh();
        Assert.False(walls.IsHidden);
        Assert.True(layers.Items[0].IsVisible);

        layers.SetAllVisible(false);
        Assert.True(walls.IsHidden && floors.IsHidden);
        Assert.Equal("Hide", session.UndoName);
    }
}
