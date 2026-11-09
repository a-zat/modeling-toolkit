using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The edits of the open document, from the properties panel and the commands, and their undo and redo.</summary>
public sealed class EditingTests
{
    [Fact]
    public async Task NothingCanBeUndoneBeforeAnEdit()
    {
        MainWindowViewModel viewModel = await OpenBox();

        Assert.False(viewModel.UndoCommand.CanExecute(null));
        Assert.False(viewModel.RedoCommand.CanExecute(null));
        Assert.False(viewModel.DeleteCommand.CanExecute(null)); // nothing selected
        Assert.StartsWith("Undo (", viewModel.UndoTip, StringComparison.Ordinal);
        Assert.Equal("Box.glb — Modeling Toolkit", viewModel.Title);
    }

    [Fact]
    public async Task RenamingInThePropertiesUndoesAndRedoes()
    {
        MainWindowViewModel viewModel = await OpenBox();
        ModelNodeViewModel mesh = SelectMesh(viewModel);

        viewModel.Properties.EditedName = "  Crate ";

        Assert.Equal("Crate", mesh.Entity!.Name);
        Assert.Equal("Crate", mesh.Name);
        Assert.Equal("Crate", viewModel.Properties.EditedName);
        Assert.StartsWith("Undo Rename (", viewModel.UndoTip, StringComparison.Ordinal);
        Assert.StartsWith("• Box.glb", viewModel.Title, StringComparison.Ordinal);

        viewModel.UndoCommand.Execute(null);
        Assert.Null(mesh.Entity.Name);
        Assert.StartsWith("Redo Rename (", viewModel.RedoTip, StringComparison.Ordinal);
        Assert.Equal("Box.glb — Modeling Toolkit", viewModel.Title);

        viewModel.RedoCommand.Execute(null);
        Assert.Equal("Crate", mesh.Entity.Name);
    }

    [Fact]
    public async Task TypingTheSameNameMakesNoCommand()
    {
        MainWindowViewModel viewModel = await OpenBox();
        SelectMesh(viewModel);

        viewModel.Properties.EditedName = "";

        Assert.False(viewModel.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChoosingAMaterialChangesItAndDefaultTakesItAway()
    {
        MainWindowViewModel viewModel = await OpenBox();
        var mesh = (MeshEntity)SelectMesh(viewModel).Entity!;
        Material own = mesh.Material!;
        Assert.Equal(["Default", own.Name], viewModel.Properties.MaterialChoices.Select(choice => choice.Name));
        Assert.Same(own, viewModel.Properties.SelectedMaterial!.Material);

        viewModel.Properties.SelectedMaterial = null; // as the list does when its items change
        Assert.False(viewModel.UndoCommand.CanExecute(null));

        viewModel.Properties.SelectedMaterial = MaterialChoice.Default;
        Assert.Null(mesh.Material);
        Assert.Same(MaterialChoice.Default, viewModel.Properties.SelectedMaterial);

        viewModel.UndoCommand.Execute(null);
        Assert.Same(own, mesh.Material);
    }

    [Fact]
    public async Task DeletingTheSelectedEntityTakesItOutOfTheTreeUntilUndone()
    {
        MainWindowViewModel viewModel = await OpenBox();
        ModelNodeViewModel mesh = SelectMesh(viewModel);
        Entity entity = mesh.Entity!;
        Block block = entity.Owner!;
        Assert.True(viewModel.DeleteCommand.CanExecute(null));

        viewModel.DeleteCommand.Execute(null);

        Assert.DoesNotContain(entity, block.Entities);
        Assert.Null(viewModel.ModelTree.SelectedNode);
        Assert.Equal("Model", viewModel.Properties.Kind);
        Assert.False(viewModel.DeleteCommand.CanExecute(null));

        viewModel.UndoCommand.Execute(null);
        Assert.Contains(entity, block.Entities);
    }

    [Fact]
    public async Task TheModelCannotBeDeleted()
    {
        MainWindowViewModel viewModel = await OpenBox();

        viewModel.ModelTree.SelectedNode = viewModel.ModelTree.Model!;

        Assert.False(viewModel.DeleteCommand.CanExecute(null));
        Assert.False(viewModel.Properties.IsEditable);
    }

    private static async Task<MainWindowViewModel> OpenBox()
    {
        var viewModel = new MainWindowViewModel(new DocumentFormats([new GltfReader()], []), new NoAppearance());
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        return viewModel;
    }

    private static ModelNodeViewModel SelectMesh(MainWindowViewModel viewModel)
    {
        ModelNodeViewModel mesh = Flatten(viewModel.ModelTree.Model!).First(node => node.IsMesh);
        viewModel.ModelTree.SelectCommand.Execute(mesh.Path);
        return viewModel.ModelTree.SelectedNode!;
    }

    private static IEnumerable<ModelNodeViewModel> Flatten(ModelNodeViewModel node) =>
        [node, .. node.Children.SelectMany(Flatten)];

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }
}
