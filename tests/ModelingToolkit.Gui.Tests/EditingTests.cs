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
    public async Task TheModelIsRenamedFromItsPropertiesAndUndoingGivesBackTheName()
    {
        MainWindowViewModel viewModel = await OpenBox();
        string before = viewModel.Document!.Root.Name;
        viewModel.ModelTree.SelectCommand.Execute(new List<Entity>());
        Assert.True(viewModel.Properties.IsEditable);

        viewModel.Properties.EditedName = " Flat ";

        Assert.Equal("Flat", viewModel.Document.Root.Name);
        Assert.Equal("Flat", viewModel.ModelTree.Model!.Name);
        Assert.Equal("Flat", viewModel.Properties.Title);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(before, viewModel.Document.Root.Name);
        Assert.Equal(before, viewModel.ModelTree.Model!.Name);
    }

    [Fact]
    public async Task TheModelIsRenamedFromTheOutline()
    {
        MainWindowViewModel viewModel = await OpenBox();
        ModelNodeViewModel model = viewModel.ModelTree.Model!;

        viewModel.ModelTree.StartRenameCommand.Execute(model);
        Assert.True(model.IsRenaming);
        model.EditedName = "Flat";
        viewModel.ModelTree.CommitRenameCommand.Execute(model);

        Assert.Equal("Flat", viewModel.Document!.Root.Name);
        Assert.Equal("Flat", model.Name);
    }

    [Fact]
    public async Task ClosingAsksToSaveOnlyChangesNotSaved()
    {
        MainWindowViewModel viewModel = await OpenBox();
        var asked = new List<string>();
        SaveChoice answer = SaveChoice.Cancel;
        viewModel.AskToSave = name =>
        {
            asked.Add(name);
            return Task.FromResult(answer);
        };
        viewModel.AsksToSaveOnClose = true;
        Assert.True(await viewModel.ConfirmCloseAsync()); // nothing changed

        SelectMesh(viewModel);
        viewModel.Properties.EditedName = "Crate";
        Assert.False(await viewModel.ConfirmCloseAsync());
        Document before = viewModel.Document!;
        await viewModel.NewCommand.ExecuteAsync(null); // cancelled: the project stays
        Assert.Same(before, viewModel.Document);
        answer = SaveChoice.DontSave;
        Assert.True(await viewModel.ConfirmCloseAsync());
        Assert.Equal(["Box.glb", "Box.glb", "Box.glb"], asked);

        viewModel.AsksToSaveOnClose = false; // as in debug builds
        Assert.True(await viewModel.ConfirmCloseAsync());
        Assert.Equal(3, asked.Count);
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
        Assert.StartsWith("Box.glb*", viewModel.Title, StringComparison.Ordinal);
        Assert.Equal("Box.glb*", viewModel.DocumentLabel);

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
        PropertiesViewModel properties = viewModel.Properties;
        Assert.Same(own, properties.Material);
        Assert.Equal(own.Name, properties.MaterialName);

        properties.OpenPickerCommand.Execute(null);
        MaterialPickerViewModel picker = properties.Picker;
        Assert.True(picker.IsOpen);
        Assert.Equal(["Default", own.Name], picker.ModelItems.Select(swatch => swatch.Name));
        picker.PickCommand.Execute(picker.ModelItems[1]); // the material it has
        Assert.False(viewModel.UndoCommand.CanExecute(null));

        properties.OpenPickerCommand.Execute(null);
        picker.PickCommand.Execute(picker.ModelItems[0]);
        Assert.False(picker.IsOpen);
        Assert.Null(mesh.Material);
        Assert.Equal("Default", properties.MaterialName);

        viewModel.UndoCommand.Execute(null);
        Assert.Same(own, mesh.Material);
    }

    [Fact]
    public async Task AMaterialOfTheLibraryIsAddedToTheDocumentWhenUsedAndReusedAfterwards()
    {
        MainWindowViewModel viewModel = await OpenBox();
        var mesh = (MeshEntity)SelectMesh(viewModel).Entity!;
        MaterialPickerViewModel picker = viewModel.Properties.Picker;
        int before = viewModel.Document!.Materials.Count;

        viewModel.Properties.OpenPickerCommand.Execute(null);
        picker.ShowCategoryCommand.Execute("Metal");
        MaterialSwatch brass = picker.LibraryItems.Single(swatch => swatch.Name == "Brass");
        picker.PickCommand.Execute(brass);

        Material added = viewModel.Document.Materials[^1];
        Assert.Equal(before + 1, viewModel.Document.Materials.Count);
        Assert.Same(added, mesh.Material);
        Assert.Equal(("Brass", 1f), (added.Name, added.Metallic));

        // Another brass of the document, of other values, keeps its name: the new one takes a number.
        viewModel.Document.Materials.Add(new Material("Gold"));
        viewModel.Properties.OpenPickerCommand.Execute(null);
        picker.PickCommand.Execute(picker.LibraryItems.Single(swatch => swatch.Name == "Gold"));
        Assert.Equal("Gold 2", mesh.Material!.Name);
        viewModel.UndoCommand.Execute(null);
        viewModel.Document.Materials.RemoveAt(viewModel.Document.Materials.Count - 1);

        viewModel.Properties.OpenPickerCommand.Execute(null);
        picker.PickCommand.Execute(picker.ModelItems[0]);
        picker.PickCommand.Execute(brass); // made before: the same material
        Assert.Same(added, mesh.Material);
        Assert.Equal(before + 1, viewModel.Document.Materials.Count);

        viewModel.UndoCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(before, viewModel.Document.Materials.Count); // the first painting undone, with the addition
    }

    [Fact]
    public async Task AnImageMakesAMaterialWithATextureOfTheSizeGiven()
    {
        MainWindowViewModel viewModel = await OpenBox();
        var mesh = (MeshEntity)SelectMesh(viewModel).Entity!;
        MaterialPickerViewModel picker = viewModel.Properties.Picker;
        string image = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        int textures = viewModel.Document!.Textures.Count;
        try
        {
            picker.ChooseImage = () => Task.FromResult<string?>(image);
            picker.TextureSize = 250;
            viewModel.Properties.OpenPickerCommand.Execute(null);

            await picker.PickTextureCommand.ExecuteAsync(null);

            Texture texture = mesh.Material!.BaseColorTexture!;
            Assert.Equal(("image/png", 250f), (texture.MediaType, mesh.Material.TextureSize));
            Assert.Equal([1, 2, 3], texture.Data);
            Assert.Contains(texture, viewModel.Document.Textures);
            viewModel.UndoCommand.Execute(null);
            Assert.Equal(textures, viewModel.Document.Textures.Count);

            picker.ChooseImage = () => Task.FromResult<string?>(null); // cancelled
            await picker.PickTextureCommand.ExecuteAsync(null);
            Assert.False(viewModel.UndoCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(image);
        }
    }

    [Fact]
    public async Task ACustomColourMakesAMaterialOfItsOwn()
    {
        MainWindowViewModel viewModel = await OpenBox();
        var mesh = (MeshEntity)SelectMesh(viewModel).Entity!;
        MaterialPickerViewModel picker = viewModel.Properties.Picker;

        picker.CustomColor = Avalonia.Media.Color.FromRgb(0x20, 0x80, 0xC0);
        picker.Roughness = 0.3;
        picker.Metallic = 1;
        picker.PickCustomCommand.Execute(null);

        Assert.Equal("Custom 2080C0", mesh.Material!.Name);
        Assert.Equal((0.3f, 1f), (mesh.Material.Roughness, mesh.Material.Metallic));
        Assert.Equal("#2080C0", picker.CustomHex);
        picker.CustomHex = "not a colour";
        Assert.Equal("#2080C0", picker.CustomHex);
        picker.CustomHex = "#ff0000";
        Assert.Equal(Avalonia.Media.Color.FromRgb(255, 0, 0), picker.CustomColor);
        Assert.Equal(0.5271152f, mesh.Material.BaseColor.B, precision: 4); // 0xC0, linear
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
        Assert.Equal("Project", viewModel.Properties.Kind);
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
        Assert.True(viewModel.Properties.IsEditable); // it can be renamed
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
