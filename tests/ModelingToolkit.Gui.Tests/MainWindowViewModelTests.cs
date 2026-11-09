using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;
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
    public async Task WhatTheViewLeavesOutIsAddedToTheStatusBar()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        string opened = viewModel.StatusMessage;

        viewModel.ReportViewWarningsCommand.Execute(new[] { "Broken cannot be shown: no faces.", "Other: no faces." });

        Assert.Equal($"{opened} The view leaves out 2 things: Broken cannot be shown: no faces.",
            viewModel.StatusMessage);
        Assert.Equal($"Broken cannot be shown: no faces.{Environment.NewLine}Other: no faces.", viewModel.StatusDetails);
    }

    [Fact]
    public async Task OpeningAFileKeepsTheFileCommandsWaitingAndCanBeCancelled()
    {
        using var gated = new GatedFormat();
        MainWindowViewModel viewModel = Create(gated);

        Task opening = viewModel.LoadAsync("model.gated");

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.OpenCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        gated.Release();
        await opening;
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.OpenCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));

        Task again = viewModel.LoadAsync("model.gated");
        viewModel.CancelCommand.Execute(null);
        await again;
        Assert.Equal("Opening model.gated was cancelled.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task WaitingToCloseCancelsAFileBeingOpenedButNotOneBeingSaved()
    {
        using var gated = new GatedFormat();
        MainWindowViewModel viewModel = Create(gated);
        await viewModel.WhenIdleAsync();

        Task opening = viewModel.LoadAsync("model.gated");
        await viewModel.WhenIdleAsync();
        await opening;
        Assert.Equal("Opening model.gated was cancelled.", viewModel.StatusMessage);

        gated.Release();
        await viewModel.LoadAsync("model.gated");
        string path = Path.Combine(Directory.CreateTempSubdirectory("mtk-tests-").FullName, "model.mtk");
        viewModel.FileDialogs = new FixedFileDialogs(path);
        Task saving = viewModel.SaveAsCommand.ExecuteAsync(null);
        await viewModel.WhenIdleAsync();
        Assert.False(viewModel.IsBusy);
        await saving;
        Assert.Equal("Saved model.mtk (Modeling Toolkit).", viewModel.StatusMessage);
        Assert.True(File.Exists(path));
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    // A format whose reading lasts until the test lets it end, or until it is cancelled: the tests have no
    // synchronization context, so a reading that ends by itself could end between two of their checks.
    private sealed class GatedFormat : IDocumentReader, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(0);

        public string FormatName => "Gated";

        public IReadOnlyList<string> Extensions { get; } = [".gated"];

        /// <summary>Lets one reading end.</summary>
        public void Release() => _gate.Release();

        public void Dispose() => _gate.Dispose();

        public Document Read(string path, ICollection<string> warnings, IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _gate.Wait(cancellationToken);
            return new Document();
        }
    }

    [Fact]
    public async Task TheSummaryCountsOneOfAKindInTheSingular()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));

        Assert.Equal("1 mesh (1 placed) · 12 triangles · 1 material · mm · Z up", viewModel.Summary);
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
    public void SelectingAShapeShowsItsVolumeMaterialAndSize()
    {
        MainWindowViewModel viewModel = Create();
        var kernel = new OcctKernel();
        var document = new Document();
        var steel = new Material("Steel");
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(100, 200, 300)) { Material = steel });
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(10, 10, 10)) { Name = "Cube" });
        viewModel.ModelTree.Show(document);

        ModelNodeViewModel model = Assert.Single(viewModel.ModelTree.Roots);
        Assert.Equal(["Steel", "Cube"], model.Children.Select(node => node.Name));
        Assert.All(model.Children, node => Assert.True(node.IsBrep));

        viewModel.ModelTree.SelectedNode = model.Children[0];

        Assert.Equal("B-rep shape", viewModel.Properties.Kind);
        Assert.Equal(
            [
                new PropertyRow("Volume", "6,000.00 cm³"),
                new PropertyRow("Size", "100.0 × 200.0 × 300.0 mm"),
            ],
            viewModel.Properties.Rows);
        Assert.Same(steel, viewModel.Properties.SelectedMaterial?.Material);

        viewModel.ModelTree.SelectedNode = model;

        Assert.Contains(new PropertyRow("B-rep shapes", "2 (2 placed)"), viewModel.Properties.Rows);
        Assert.DoesNotContain(viewModel.Properties.Rows, row => row.Name == "Meshes");
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
    public async Task AnyFailureOfAFormatIsReportedInTheStatusBarAndTheDocumentStaysOpen()
    {
        var failing = new FailingFormat();
        var viewModel = new MainWindowViewModel(
            new DocumentFormats([new GltfReader(), failing], [new NativeDocumentWriter(), failing]),
            new RecordingAppearance());

        await viewModel.LoadAsync("model.fail");
        Assert.Equal($"Cannot open model.fail: {FailingFormat.Reason}", viewModel.StatusMessage);

        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        Document document = viewModel.Document!;
        await viewModel.SaveAsCommand.ExecuteAsync(null); // no dialogs: nothing happens
        viewModel.FileDialogs = new FixedFileDialogs("copy.fail");
        await viewModel.SaveAsCommand.ExecuteAsync(null);
        Assert.Equal($"Cannot save copy.fail: {FailingFormat.Reason} The file is as it was.", viewModel.StatusMessage);
        Assert.Same(document, viewModel.Document);
        Assert.False(viewModel.IsBusy);
    }

    // A format whose reader and writer fail in a way that no format documents.
    private sealed class FailingFormat : IDocumentReader, IDocumentWriter
    {
        public const string Reason = "Something unexpected.";

        public string FormatName => "Failing";

        public IReadOnlyList<string> Extensions { get; } = [".fail"];

        public Document Read(string path, ICollection<string> warnings, IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(Reason);

        public void Write(Document document, string path, IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(Reason);
    }

    private sealed class FixedFileDialogs(string path) : IFileDialogs
    {
        public Task<string?> OpenAsync(string title, IReadOnlyList<FileType> types) => Task.FromResult<string?>(path);

        public Task<string?> SaveAsync(string title, FileType type, string suggestedName) =>
            Task.FromResult<string?>(path);
    }

    [Fact]
    public async Task WhatAFileLeavesOutIsReportedInTheStatusBar()
    {
        MainWindowViewModel viewModel = Create();
        string folder = Directory.CreateTempSubdirectory("mtk-gui-tests-").FullName;
        string path = Path.Combine(folder, "basisu.gltf");
        const string extensions = """
            "extensionsUsed": ["KHR_texture_basisu"], "extensionsRequired": ["KHR_texture_basisu"], "asset"
            """;
        string model = File.ReadAllText(Paths.Asset("gltf", "SimpleMeshes.gltf"));
        File.WriteAllText(path, model.Replace("\"asset\"", extensions));
        try
        {
            await viewModel.LoadAsync(path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        const string warning =
            "Unsupported glTF extension KHR_texture_basisu: the textures or materials that need it are left out.";
        Assert.Equal($"Opened basisu.gltf (glTF), with a warning: {warning}", viewModel.StatusMessage);
        Assert.Equal(warning, viewModel.StatusDetails);
        Assert.NotEmpty(viewModel.ModelTree.Roots);

        await viewModel.LoadAsync("model.obj");

        Assert.Null(viewModel.StatusDetails);
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

    private static MainWindowViewModel Create(params IDocumentReader[] readers) => new(
        new DocumentFormats(
            [new GltfReader(), new NativeDocumentReader(new OcctKernel()), .. readers],
            [new NativeDocumentWriter()]),
        new RecordingAppearance());

    private static IEnumerable<ModelNodeViewModel> Flatten(ModelNodeViewModel node) =>
        [node, .. node.Children.SelectMany(Flatten)];

    [Fact]
    public void TheViewCommandsMoveTheCameraAndChangeHowTheViewShowsTheModel()
    {
        MainWindowViewModel viewModel = Create();
        var camera = new RecordingCamera();
        viewModel.Camera = camera;
        Assert.True(viewModel.IsShaded);
        Assert.True(viewModel.IsPerspective);
        Assert.True(viewModel.IsGridVisible);

        viewModel.FitAllCommand.Execute(null);
        viewModel.ShowViewCommand.Execute(StandardView.Top);
        viewModel.SetDisplayModeCommand.Execute(DisplayMode.Wireframe);
        viewModel.TogglePerspectiveCommand.Execute(null);
        viewModel.ToggleGridCommand.Execute(null);

        Assert.Equal(["fit all", "Top"], camera.Calls);
        Assert.Equal(DisplayMode.Wireframe, viewModel.DisplayMode);
        Assert.Equal((false, false, true), (viewModel.IsShaded, viewModel.IsShadedWithEdges, viewModel.IsWireframe));
        Assert.False(viewModel.IsPerspective);
        Assert.False(viewModel.IsGridVisible);
    }

    [Fact]
    public void TheSettingsChooseTheRenderStyle()
    {
        MainWindowViewModel viewModel = Create();
        Assert.Equal(RenderStyle.Standard, viewModel.RenderStyle);

        viewModel.ShowPanelCommand.Execute(SidePanel.Settings);
        viewModel.IsCadRenderStyle = true;
        viewModel.IsStandardRenderStyle = false; // the other radio button, unchecked

        Assert.True(viewModel.IsSettingsPanelActive);
        Assert.Equal(RenderStyle.Cad, viewModel.RenderStyle);
        Assert.False(viewModel.IsStandardRenderStyle);

        viewModel.SetRenderStyleCommand.Execute(RenderStyle.Standard);

        Assert.Equal((true, false), (viewModel.IsStandardRenderStyle, viewModel.IsCadRenderStyle));
    }

    private sealed class RecordingAppearance : IAppearance
    {
        public List<bool> Applied { get; } = [];

        public void Apply(bool dark) => Applied.Add(dark);
    }

    private sealed class RecordingCamera : IViewCamera
    {
        public List<string> Calls { get; } = [];

        public void FitAll() => Calls.Add("fit all");

        public void ShowView(StandardView view) => Calls.Add(view.ToString());
    }
}
