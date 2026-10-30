using Microsoft.Extensions.Logging.Abstractions;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Gui.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task OpeningAModelShowsItsStructureMaterialsAndSummary()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));

        Assert.Equal("CesiumMilkTruck.glb", viewModel.DocumentName);
        Assert.Equal("4 meshes (5 placed) · 3,624 triangles · 4 materials · mm · Z up", viewModel.Summary);
        ModelNodeViewModel model = Assert.Single(viewModel.ModelTree.Roots);
        Assert.Equal(2, Flatten(model).Count(node => node.Entity is BlockReference { Block.Name: "Wheels" }));
        Assert.Equal(4, viewModel.Materials.Items.Count);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectingAMeshShowsItsProperties()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));

        viewModel.ModelTree.SelectedNode = Flatten(viewModel.ModelTree.Roots[0]).First(node => node.IsMesh);

        Assert.True(viewModel.Properties.HasSelection);
        Assert.Equal("Mesh", viewModel.Properties.Kind);
        Assert.Contains(new PropertyRow("Triangles", "12"), viewModel.Properties.Rows);
        Assert.Contains(new PropertyRow("Size", "1,000.0 × 1,000.0 × 1,000.0 mm"), viewModel.Properties.Rows);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsReportedInTheStatusBar()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.LoadAsync("model.obj");

        Assert.Equal(
            "Cannot open model.obj: '.obj' files cannot be read. Supported: .glb, .gltf, .mtk.", viewModel.StatusMessage);
        Assert.Empty(viewModel.ModelTree.Roots);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void ChoosingAThemeAppliesIt()
    {
        var appearance = new RecordingAppearance();
        var viewModel = new MainWindowViewModel(new DocumentFormats([], []), appearance);

        viewModel.Theme = ThemeKind.Semi;
        viewModel.IsDark = false;

        Assert.Equal([(ThemeKind.Semi, true, true), (ThemeKind.Semi, false, true)], appearance.Applied);
    }

    [Fact]
    public void ThePaletteFindsCommandsByName()
    {
        CommandPaletteViewModel palette = Create().Palette;

        palette.OpenCommand.Execute(null);
        palette.Query = "layout";

        Assert.True(palette.IsOpen);
        Assert.Equal(["Layout A: Classic", "Layout B: Compact"], palette.Results.Select(command => command.Name));
    }

    private static MainWindowViewModel Create() => new(
        new DocumentFormats(
            [new GltfReader(NullLogger<GltfReader>.Instance), new NativeDocumentReader()],
            [new NativeDocumentWriter()]),
        new RecordingAppearance());

    private static IEnumerable<ModelNodeViewModel> Flatten(ModelNodeViewModel node) =>
        [node, .. node.Children.SelectMany(Flatten)];

    private sealed class RecordingAppearance : IAppearance
    {
        public List<(ThemeKind Theme, bool Dark, bool Compact)> Applied { get; } = [];

        public void Apply(ThemeKind theme, bool dark, bool compact) => Applied.Add((theme, dark, compact));
    }
}
