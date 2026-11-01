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
    public async Task MeshesWithoutANameAreNamedAfterTheirMaterial()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));

        Assert.Equal(
            ["wheels", "wheels", "truck", "glass", "window_trim"],
            Flatten(viewModel.ModelTree.Roots[0]).Where(node => node.IsMesh).Select(node => node.Name));
    }

    [Fact]
    public async Task EachPlacementOfABlockHasANodeWithItsOwnPath()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
        ModelNodeViewModel model = viewModel.ModelTree.Roots[0];

        ModelNodeViewModel[] wheels =
        [
            .. Flatten(model).Where(node =>
                node.Entity is MeshEntity && node.Path[^2] is BlockReference { Block.Name: "Wheels" }),
        ];

        Assert.Empty(model.Path);
        Assert.Equal(2, wheels.Length);
        Assert.Same(wheels[0].Entity, wheels[1].Entity);
        Assert.NotEqual(wheels[0].Path[^3], wheels[1].Path[^3]);
        Assert.Same(viewModel.Document!.Root.Entities[0], wheels[0].Path[0]);
    }

    [Fact]
    public async Task PickingAPathOpensTheTreeDownToItsNode()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
        // The first mesh of the document, under the first reference of every block on the way.
        var path = new List<Entity>();
        Entity entity = viewModel.Document!.Root.Entities[0];
        for (; entity is BlockReference reference; entity = reference.Block.Entities[0])
        {
            path.Add(entity);
        }
        path.Add(entity);

        viewModel.ModelTree.SelectCommand.Execute(path);

        ModelNodeViewModel node = viewModel.ModelTree.Roots[0];
        foreach (Entity step in path)
        {
            Assert.True(node.IsExpanded);
            node = node.Children.Single(child => child.Entity == step);
        }
        Assert.Same(node, viewModel.ModelTree.SelectedNode);
        Assert.Equal("Mesh", viewModel.Properties.Kind);

        viewModel.ModelTree.SelectCommand.Execute(null);

        Assert.Null(viewModel.ModelTree.SelectedNode);
        Assert.False(viewModel.Properties.HasSelection);
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
    public void SwitchingToTheDarkVariantAppliesIt()
    {
        var appearance = new RecordingAppearance();
        var viewModel = new MainWindowViewModel(new DocumentFormats([], []), appearance);

        viewModel.ToggleDarkCommand.Execute(null);

        Assert.True(viewModel.IsDark);
        Assert.Equal([true], appearance.Applied);
    }

    [Fact]
    public void ThePaletteFindsCommandsByName()
    {
        CommandPaletteViewModel palette = Create().Palette;

        palette.OpenCommand.Execute(null);
        palette.Query = "shaded";

        Assert.True(palette.IsOpen);
        Assert.Equal(["Shaded", "Shaded with Edges"], palette.Results.Select(command => command.Name));
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
        public List<bool> Applied { get; } = [];

        public void Apply(bool dark) => Applied.Add(dark);
    }
}
