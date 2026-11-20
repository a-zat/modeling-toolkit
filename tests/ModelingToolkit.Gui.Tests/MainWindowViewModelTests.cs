using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Editing.Tools;
using Avalonia.Input;
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
    public async Task OpeningAModelShowsItsStructureAndSummary()
    {
        MainWindowViewModel viewModel = Create();

        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));

        Assert.Equal("CesiumMilkTruck.glb", viewModel.DocumentName);
        Assert.Equal("4 meshes (5 placed) · 3,624 triangles · 4 materials · mm · Z up", viewModel.Summary);
        ModelNodeViewModel model = viewModel.ModelTree.Model!;
        Assert.Equal(2, Flatten(model).Count(node => node.Entity is BlockReference { Block.Name: "Wheels" }));
        Assert.Equal(4, viewModel.Document!.Materials.Count);
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

        viewModel.ModelTree.SelectedNode = Flatten(viewModel.ModelTree.Model!).First(node => node.IsMesh);

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
            Flatten(viewModel.ModelTree.Model!).Where(node => node.IsMesh).Select(node => node.Name));
    }

    [Fact]
    public async Task EachPlacementOfABlockHasANodeWithItsOwnPath()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
        ModelNodeViewModel model = viewModel.ModelTree.Model!;

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

        // The mesh is in the last block, in its geometry, opened too, when the block has several meshes.
        ModelNodeViewModel node = viewModel.ModelTree.Model!;
        foreach (Entity step in path)
        {
            Assert.True(node.IsExpanded);
            if (step is not BlockReference && node.Children.SingleOrDefault(child => child.IsGeometry) is { } geometry)
            {
                node = geometry;
                Assert.True(node.IsExpanded);
            }
            node = node.Children.Single(child => child.Entity == step);
        }
        Assert.Same(node, viewModel.ModelTree.SelectedNode);
        Assert.Equal("Mesh", viewModel.Properties.Kind);

        viewModel.ModelTree.SelectCommand.Execute(null);

        Assert.Null(viewModel.ModelTree.SelectedNode);
        Assert.Equal("Project", viewModel.Properties.Kind);
    }

    [Fact]
    public void SelectingAShapeShowsItsVolumeMaterialAndSize()
    {
        MainWindowViewModel viewModel = Create();
        var kernel = new OcctKernel();
        var document = new Document();
        var steel = new Material("Steel");
        // All the faces of the first box painted with steel: its material.
        document.Root.Entities.Add(new BrepEntity(
            kernel, kernel.MakeBox(100, 200, 300), Enumerable.Range(0, 6).ToDictionary(face => face, _ => steel)));
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(10, 10, 10)) { Name = "Cube" });
        viewModel.ModelTree.Show(document);

        ModelNodeViewModel model = viewModel.ModelTree.Model!;
        ModelNodeViewModel geometry = Assert.Single(model.Children);
        Assert.Equal(("Geometry", "2 shapes"), (geometry.Name, geometry.Detail));
        Assert.Equal("1 solid · 6 faces", geometry.Children[0].Detail);
        Assert.Equal(["Geometry", "Cube"], geometry.Children.Select(node => node.Name)); // unnamed, then named
        Assert.All(geometry.Children, node => Assert.True(node.IsBrep));

        viewModel.ModelTree.SelectedNode = geometry;

        Assert.Equal(("Geometry", "of Model"), (viewModel.Properties.Title, viewModel.Properties.Kind));
        Assert.Equal(
            [
                new PropertyRow("B-rep shapes", "2"),
                new PropertyRow("Solids", "2"),
                new PropertyRow("Faces", "12"),
                new PropertyRow("Edges", "24"),
                new PropertyRow("Vertices", "16"),
            ],
            viewModel.Properties.Rows);
        Assert.False(viewModel.Properties.IsEditable);

        viewModel.ModelTree.SelectedNode = geometry.Children[0];

        Assert.Equal("B-rep shape", viewModel.Properties.Kind);
        Assert.Equal(
            [
                new PropertyRow("Volume", "6,000.00 cm³"),
                new PropertyRow("Size", "100.0 × 200.0 × 300.0 mm"),
            ],
            viewModel.Properties.Rows);
        Assert.Same(steel, viewModel.Properties.Material);

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
    public async Task WalkingKeepsTheToolsOutOfTheViewAndEscapeStopsIt()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        Assert.NotEmpty(viewModel.ToolHint);

        // The walk waits for a click that chooses where to stand, and the inspector closes as it begins.
        IReadOnlyList<Entity> mesh = Flatten(viewModel.ModelTree.Model!).Last().Path;
        viewModel.ToggleWalkCommand.Execute(null);
        Assert.True(viewModel.IsPlacingWalk);
        Assert.False(viewModel.IsWalking);
        Assert.True(viewModel.IsInspectorOpen);
        viewModel.ViewInputCommand.Execute(new ViewportPick(mesh, null, new Point3d(1, 2, 3), new System.Numerics.Vector3(0, 0, 1),
            KeyModifiers.None, 1, ViewportInputKind.Click));
        Assert.False(viewModel.IsPlacingWalk);
        Assert.True(viewModel.IsWalking);
        Assert.False(viewModel.IsInspectorOpen);
        Assert.Equal(new Point3d(1, 2, 3), viewModel.WalkStart);
        Assert.Empty(viewModel.ToolHint);

        // A click on a part that nothing moves selects nothing, as the select tool would.
        viewModel.ViewInputCommand.Execute(new ViewportPick(mesh, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click));
        Assert.Null(viewModel.ModelTree.SelectedNode);
        Assert.Equal("Nothing there can be used.", viewModel.StatusMessage);

        viewModel.EscapeCommand.Execute(null);
        Assert.False(viewModel.IsWalking);
        Assert.NotEmpty(viewModel.ToolHint);

        // Escape also cancels the wait for the place.
        viewModel.ToggleWalkCommand.Execute(null);
        viewModel.EscapeCommand.Execute(null);
        Assert.False(viewModel.IsPlacingWalk);
        Assert.False(viewModel.IsWalkActive);
    }

    [Fact]
    public async Task WalkingShowsNothingSelectedAndTheSelectionComesBackAfter()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        ModelNodeViewModel mesh = Flatten(viewModel.ModelTree.Model!).Last();
        viewModel.ModelTree.SelectedNode = mesh;
        Assert.NotNull(viewModel.EmphasizedPath);

        viewModel.ToggleWalkCommand.Execute(null);
        Assert.Null(viewModel.EmphasizedPath);
        viewModel.ViewInputCommand.Execute(new ViewportPick(mesh.Path, null, new Point3d(1, 2, 3),
            new System.Numerics.Vector3(0, 0, 1), KeyModifiers.None, 1, ViewportInputKind.Click));
        Assert.Null(viewModel.EmphasizedPath);
        Assert.Empty(viewModel.ShownElements);
        Assert.Empty(viewModel.Overlay.Lines);

        viewModel.EscapeCommand.Execute(null);
        Assert.NotNull(viewModel.EmphasizedPath);
    }

    [Fact]
    public async Task TheRenderedViewIsOnlyToLookAtAClickOpensOrClosesAndTheToolsWait()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        IReadOnlyList<Entity> mesh = Flatten(viewModel.ModelTree.Model!).Last().Path;
        viewModel.ModelTree.SelectedNode = Flatten(viewModel.ModelTree.Model!).Last();
        Assert.True(viewModel.ActiveTool.IsActive);

        viewModel.SetDisplayModeCommand.Execute(DisplayMode.Rendered);

        Assert.False(viewModel.IsEditable);
        Assert.False(viewModel.ActiveTool.IsActive);
        Assert.Empty(viewModel.ToolHint);
        Assert.Null(viewModel.EmphasizedPath); // nothing highlighted
        viewModel.ViewInputCommand.Execute(new ViewportPick(mesh, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click));
        Assert.Equal("Nothing there can be used.", viewModel.StatusMessage);

        viewModel.SetDisplayModeCommand.Execute(DisplayMode.Shaded);
        Assert.True(viewModel.IsEditable);
        Assert.True(viewModel.ActiveTool.IsActive);
        Assert.NotEmpty(viewModel.ToolHint);
        Assert.NotNull(viewModel.EmphasizedPath);
    }

    [Fact]
    public void TheDisplayModesAreASelectorAndTheDefaultIsShadedWithEdges()
    {
        MainWindowViewModel viewModel = Create();
        Assert.Equal(DisplayMode.ShadedWithEdges, viewModel.DisplayMode);
        Assert.True(viewModel.IsShadedWithEdges);

        // A button of the selector, switched off while chosen, leaves the choice as it is.
        viewModel.IsShadedWithEdges = false;
        Assert.Equal(DisplayMode.ShadedWithEdges, viewModel.DisplayMode);

        viewModel.IsWireframe = true;
        Assert.Equal(DisplayMode.Wireframe, viewModel.DisplayMode);
        Assert.False(viewModel.IsShadedWithEdges);
        viewModel.IsRendered = true;
        Assert.True(viewModel.IsRendered);
        Assert.False(viewModel.IsWireframe);
    }

    [Fact]
    public async Task ASelectionInTheViewIsFramedByItsBoxOneInTheOutlineIsShownInFrontOfTheRest()
    {
        MainWindowViewModel viewModel = Create();
        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
        ModelNodeViewModel reference = Flatten(viewModel.ModelTree.Model!).First(node => node.IsReference);

        // A click in the 3D view, through the select tool.
        viewModel.ViewInputCommand.Execute(new ViewportPick(reference.Path, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click));
        Assert.True(viewModel.SelectedInView);
        Assert.Null(viewModel.EmphasizedPath);
        Assert.Equal(12, viewModel.Overlay.Lines.Count(line => line.Width == 1.5)); // its box, besides the arrows to turn

        viewModel.ModelTree.SelectedNode = null;
        viewModel.ModelTree.SelectedNode = reference;
        Assert.False(viewModel.SelectedInView);
        Assert.Equal(reference.Path, viewModel.EmphasizedPath);
        Assert.Equal(3, viewModel.Overlay.Lines.Count); // no box, its axes
    }

    [Fact]
    public async Task AModelImportedFollowsThePointerByItsOriginUntilAClickPlacesItSelected()
    {
        MainWindowViewModel viewModel = CreateWithKernel();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));

        await viewModel.ImportAsync(Paths.Asset("gltf", "BoxTextured.glb"));

        SelectTool select = Assert.IsType<SelectTool>(viewModel.ActiveTool);
        Assert.True(select.IsPlacing);
        Assert.True(viewModel.SelectedInView);
        Assert.Null(viewModel.EmphasizedPath);
        BlockReference imported = Assert.IsType<BlockReference>(viewModel.ModelTree.SelectedNode!.Entity);
        var down = new PointerRay(new Point3d(500, 300, 1000), new Vector3d(0, 0, -1), 1, 0);
        viewModel.ViewInputCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Hover, down));
        viewModel.ViewInputCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click, down));

        Assert.False(select.IsPlacing);
        Assert.Equal(new Point3d(500, 300, 0), imported.Transform.Apply(Point3d.Origin));
        Assert.Same(imported, viewModel.ModelTree.SelectedNode?.Entity);
        Assert.NotNull(select.Handles);
    }

    [Fact]
    public async Task EscapeWhileAModelImportedFollowsThePointerTakesItOut()
    {
        MainWindowViewModel viewModel = CreateWithKernel();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        int before = viewModel.Document!.Root.Entities.Count;

        await viewModel.ImportAsync(Paths.Asset("gltf", "BoxTextured.glb"));
        viewModel.EscapeCommand.Execute(null);

        Assert.False(((SelectTool)viewModel.ActiveTool).IsPlacing);
        Assert.Equal(before, viewModel.Document.Root.Entities.Count);
    }

    [Fact]
    public async Task EscapeBringsWhatTheOutlineShowsInFrontOfTheRestBackAmongIt()
    {
        MainWindowViewModel viewModel = CreateWithKernel();
        await viewModel.LoadAsync(Paths.Asset("gltf", "CesiumMilkTruck.glb"));
        ModelNodeViewModel reference = Flatten(viewModel.ModelTree.Model!).First(node => node.IsReference);
        viewModel.ActivateToolCommand.Execute(viewModel.Tools.First(item => item.Tool is MoveTool));

        viewModel.ModelTree.SelectedNode = reference;
        Assert.NotNull(viewModel.EmphasizedPath);
        Assert.Equal(3, viewModel.Overlay.Lines.Count); // its axes
        viewModel.EscapeCommand.Execute(null);

        Assert.True(viewModel.SelectedInView);
        Assert.Null(viewModel.EmphasizedPath);
        Assert.Same(reference, viewModel.ModelTree.SelectedNode);
    }

    [Fact]
    public async Task TabPassesBetweenMoveAndRotateKeepingThePointTaken()
    {
        MainWindowViewModel viewModel = CreateWithKernel();
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        await viewModel.ImportAsync(Paths.Asset("gltf", "BoxTextured.glb"));
        viewModel.EscapeCommand.Execute(null); // not placed: taken out
        await viewModel.ImportAsync(Paths.Asset("gltf", "BoxTextured.glb"));
        var down = new PointerRay(new Point3d(0, 0, 1000), new Vector3d(0, 0, -1), 1, 0);
        viewModel.ViewInputCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click, down)); // placed where it was
        Assert.False(viewModel.SwitchMoveRotate()); // the select tool

        viewModel.ActivateToolCommand.Execute(viewModel.Tools.First(item => item.Tool is MoveTool));
        viewModel.ViewInputCommand.Execute(new ViewportPick(null, null, default, default, KeyModifiers.None, 1,
            ViewportInputKind.Click, down));
        Point3d? taken = Assert.IsType<MoveTool>(viewModel.ActiveTool).TakenAt;
        Assert.NotNull(taken);
        Assert.True(viewModel.SwitchMoveRotate());

        Assert.Equal(taken, Assert.IsType<RotateTool>(viewModel.ActiveTool).TakenAt);
        Assert.True(viewModel.SwitchMoveRotate());
        Assert.Equal(taken, Assert.IsType<MoveTool>(viewModel.ActiveTool).TakenAt);
    }

    private static MainWindowViewModel CreateWithKernel()
    {
        var kernel = new OcctKernel();
        return new MainWindowViewModel(
            new DocumentFormats([new GltfReader(), new NativeDocumentReader(kernel)], [new NativeDocumentWriter()]),
            new RecordingAppearance(), kernel);
    }

    [Fact]
    public void TheApplicationNeverStartsRendered()
    {
        var store = new MemorySettingsStore(new AppSettings { DisplayMode = DisplayMode.Rendered, Collisions = false });

        var viewModel = new MainWindowViewModel(new DocumentFormats([], []), new RecordingAppearance(), settings: store);

        Assert.Equal(DisplayMode.ShadedWithEdges, viewModel.DisplayMode);
        Assert.False(viewModel.Collisions);
    }

    [Fact]
    public void TheSettingsAreReadAtStartAndWrittenWhenChanged()
    {
        var store = new MemorySettingsStore(new AppSettings { DarkTheme = true, UseGlb = false, TimeOfDay = 18 });
        var viewModel = new MainWindowViewModel(
            new DocumentFormats([], []), new RecordingAppearance(), settings: store);
        Assert.True(viewModel.IsDark);
        Assert.False(viewModel.UseGlb);
        Assert.Equal(18, viewModel.TimeOfDay);

        viewModel.IsGridVisible = false;
        viewModel.UseGlb = true;
        viewModel.Latitude = 41.9;
        viewModel.Collisions = false;
        viewModel.SunDate = new DateTime(2026, 12, 21);
        viewModel.Cloudiness = 0.8;
        viewModel.EdgeWidth = 2;
        viewModel.EyeHeight = 1750;
        viewModel.LookSensitivity = 0.5;

        Assert.Equal(new AppSettings
        {
            DarkTheme = true, UseGlb = true, TimeOfDay = 18, Grid = false, Latitude = 41.9, Collisions = false,
            SunDate = new DateOnly(2026, 12, 21), Cloudiness = 0.8, EdgeWidth = 2, EyeHeight = 1750, LookSensitivity = 0.5,
        }, store.Load());
        // What the view walks with follows, the look sensitivity in radians.
        Assert.Equal(1750, viewModel.WalkSettings.EyeHeight);
        Assert.Equal(0.5 * Math.PI / 180, viewModel.WalkSettings.LookSensitivity, 1e-12);
    }

    [Fact]
    public async Task InTheGlbModeSaveWritesTheGlbOpenedWhoseRootNodeIsTheModel()
    {
        string folder = Directory.CreateTempSubdirectory("mtk-glb-mode-").FullName;
        try
        {
            string path = Path.Combine(folder, "truck.glb");
            File.Copy(Paths.Asset("gltf", "CesiumMilkTruck.glb"), path);
            MainWindowViewModel viewModel = CreateWithWriters();
            Assert.True(viewModel.UseGlb);
            await viewModel.LoadAsync(path);
            Entity top = Assert.Single(viewModel.Document!.Root.Entities);
            DateTime before = File.GetLastWriteTimeUtc(path);

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.NotEqual(before, File.GetLastWriteTimeUtc(path));
            await viewModel.LoadAsync(path);
            // The root node written, holding the model, is not shown as a block of its own.
            Assert.Equal(top.Name, Assert.Single(viewModel.Document!.Root.Entities).Name);
            Assert.Single(viewModel.Document.Animations);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutTheGlbModeAGlbOpenedIsNotOverwritten()
    {
        MainWindowViewModel viewModel = CreateWithWriters();
        viewModel.UseGlb = false;
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        DateTime before = File.GetLastWriteTimeUtc(Paths.Asset("gltf", "Box.glb"));

        await viewModel.SaveCommand.ExecuteAsync(null); // no file dialogs: Save As does nothing

        Assert.Equal(before, File.GetLastWriteTimeUtc(Paths.Asset("gltf", "Box.glb")));
    }

    private static MainWindowViewModel CreateWithWriters() => new(
        new DocumentFormats(
            [new GltfReader(), new NativeDocumentReader(new OcctKernel())],
            [new GltfWriter(), new NativeDocumentWriter()]),
        new RecordingAppearance());

    [Fact]
    public void TheViewCommandsMoveTheCameraAndChangeHowTheViewShowsTheModel()
    {
        MainWindowViewModel viewModel = Create();
        var camera = new RecordingCamera();
        viewModel.Camera = camera;
        Assert.True(viewModel.IsShadedWithEdges);
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

        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.IsCadRenderStyle = true;
        viewModel.IsStandardRenderStyle = false; // the other radio button, unchecked

        Assert.True(viewModel.IsSettingsOpen);
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
