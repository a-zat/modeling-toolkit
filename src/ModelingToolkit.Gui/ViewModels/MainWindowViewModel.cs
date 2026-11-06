using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>State and commands of the main window.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string ApplicationName = "Modeling Toolkit";

    private readonly DocumentFormats _formats;
    private readonly IAppearance _appearance;
    private readonly bool _initialized;
    private string? _path;

    public MainWindowViewModel(DocumentFormats formats, IAppearance appearance)
    {
        _formats = formats;
        _appearance = appearance;

        Title = ApplicationName;
        DocumentName = "No document";
        Summary = "Open a model with File › Open";
        StatusMessage = string.Empty;
        ActivePanel = SidePanel.Model;
        IsDark = false;
        IsPerspective = true;
        IsGridVisible = true;

        ModelTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModelTreeViewModel.SelectedNode))
            {
                Properties.Show(ModelTree.SelectedNode);
            }
        };
        Palette.Commands =
        [
            new("Open…", OpenCommand, Gesture: Shortcut("O")),
            new("Save", SaveCommand, Gesture: Shortcut("S")),
            new("Save As…", SaveAsCommand, Gesture: Shortcut("S", shift: true)),
            new("Fit All", FitAllCommand),
            new("Top View", ShowViewCommand, StandardView.Top),
            new("Bottom View", ShowViewCommand, StandardView.Bottom),
            new("Front View", ShowViewCommand, StandardView.Front),
            new("Back View", ShowViewCommand, StandardView.Back),
            new("Left View", ShowViewCommand, StandardView.Left),
            new("Right View", ShowViewCommand, StandardView.Right),
            new("Isometric View", ShowViewCommand, StandardView.Isometric),
            new("Shaded", SetDisplayModeCommand, DisplayMode.Shaded),
            new("Shaded with Edges", SetDisplayModeCommand, DisplayMode.ShadedWithEdges),
            new("Wireframe", SetDisplayModeCommand, DisplayMode.Wireframe),
            new("Toggle Perspective", TogglePerspectiveCommand),
            new("Toggle Grid", ToggleGridCommand),
            new("Toggle Dark Theme", ToggleDarkCommand),
        ];
        _initialized = true;
    }

    public ModelTreeViewModel ModelTree { get; } = new();

    public PropertiesViewModel Properties { get; } = new();

    public MaterialsViewModel Materials { get; } = new();

    public CommandPaletteViewModel Palette { get; } = new();

    /// <summary>A shortcut as the platform writes it: ⇧⌘S on macOS, Ctrl+Shift+S elsewhere.</summary>
    internal static string Shortcut(string key, bool shift = false) => OperatingSystem.IsMacOS()
        ? $"{(shift ? "⇧" : "")}⌘{key}"
        : $"Ctrl+{(shift ? "Shift+" : "")}{key}";

    /// <summary>The open and save dialogs, provided by the window.</summary>
    public IFileDialogs? FileDialogs { get; set; }

    /// <summary>The camera of the 3D view, provided by the layout that shows it.</summary>
    public IViewCamera? Camera { get; set; }

    /// <summary>The open document, shown in the 3D view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand))]
    public partial Document? Document { get; private set; }

    public bool HasDocument => Document is not null;

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string DocumentName { get; private set; }

    /// <summary>What the document contains, for the status bar.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; }

    /// <summary>Outcome of the last operation, for the status bar.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; }

    /// <summary>All the warnings of the last opened file, one per line, when the status message reports them.</summary>
    [ObservableProperty]
    public partial string? StatusDetails { get; private set; }

    // One side panel at a time, chosen in the activity bar.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidePanelOpen), nameof(IsModelPanelActive), nameof(IsMaterialsPanelActive))]
    public partial SidePanel ActivePanel { get; set; }

    public bool IsSidePanelOpen => ActivePanel != SidePanel.None;

    public bool IsModelPanelActive => ActivePanel == SidePanel.Model;

    public bool IsMaterialsPanelActive => ActivePanel == SidePanel.Materials;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    // How the 3D view shows the document.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShaded), nameof(IsShadedWithEdges), nameof(IsWireframe))]
    public partial DisplayMode DisplayMode { get; set; }

    public bool IsShaded => DisplayMode == DisplayMode.Shaded;

    public bool IsShadedWithEdges => DisplayMode == DisplayMode.ShadedWithEdges;

    public bool IsWireframe => DisplayMode == DisplayMode.Wireframe;

    /// <summary>Perspective, or orthographic (parallel) projection.</summary>
    [ObservableProperty]
    public partial bool IsPerspective { get; set; }

    /// <summary>Whether the grid on the ground and the axes through the origin are shown.</summary>
    [ObservableProperty]
    public partial bool IsGridVisible { get; set; }

    /// <summary>Applies the variant of the theme chosen here.</summary>
    public void ApplyAppearance() => _appearance.Apply(IsDark);

    /// <summary>Reads a model and shows it; failures are reported in the status bar.</summary>
    public async Task LoadAsync(string path)
    {
        string name = Path.GetFileName(path);
        StatusMessage = $"Opening {name}…";
        try
        {
            IDocumentReader reader = _formats.ReaderFor(path);
            var warnings = new List<string>();
            Document document = await Task.Run(() => reader.Read(path, warnings));
            Show(document, path);
            StatusMessage = warnings.Count switch
            {
                0 => $"Opened {name} ({reader.FormatName}).",
                1 => $"Opened {name} ({reader.FormatName}), with a warning: {warnings[0]}",
                _ => $"Opened {name} ({reader.FormatName}), with {warnings.Count} warnings: {warnings[0]}",
            };
            StatusDetails = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings) : null;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = $"Cannot open {name}: {exception.Message}";
        }
    }

    partial void OnStatusMessageChanged(string value) => StatusDetails = null;

    // The initial value is applied by the application, once Avalonia is ready.
    partial void OnIsDarkChanged(bool value)
    {
        if (_initialized)
        {
            ApplyAppearance();
        }
    }

    [RelayCommand]
    private async Task Open()
    {
        if (FileDialogs is null)
        {
            return;
        }

        List<FileType> types =
        [
            new("3D models", [.. _formats.Readers.SelectMany(reader => reader.Extensions)]),
            .. _formats.Readers.Select(reader => new FileType(reader.FormatName, reader.Extensions)),
        ];
        string? path = await FileDialogs.OpenAsync("Open a model", types);
        if (path is not null)
        {
            await LoadAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Save()
    {
        if (_path is not null && IsWritable(_path))
        {
            Write(_path);
        }
        else
        {
            await SaveAs();
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAs()
    {
        if (FileDialogs is null)
        {
            return;
        }

        IDocumentWriter writer = _formats.Writers[0];
        string suggestedName = Path.ChangeExtension(Path.GetFileName(_path) ?? "model", writer.Extensions[0]);
        string? path = await FileDialogs.SaveAsync(
            "Save the model", new FileType(writer.FormatName, writer.Extensions), suggestedName);
        if (path is not null)
        {
            Write(path);
        }
    }

    [RelayCommand]
    private void FitAll() => Camera?.FitAll();

    [RelayCommand]
    private void ShowView(StandardView view) => Camera?.ShowView(view);

    [RelayCommand]
    private void SetDisplayMode(DisplayMode mode) => DisplayMode = mode;

    [RelayCommand]
    private void TogglePerspective() => IsPerspective = !IsPerspective;

    [RelayCommand]
    private void ToggleGrid() => IsGridVisible = !IsGridVisible;

    [RelayCommand]
    private void ToggleDark() => IsDark = !IsDark;

    // Selecting the active panel again closes it, as in VS Code.
    [RelayCommand]
    private void ShowPanel(SidePanel panel) => ActivePanel = ActivePanel == panel ? SidePanel.None : panel;

    private void Show(Document document, string path)
    {
        Document = document;
        _path = path;
        DocumentName = Path.GetFileName(path);
        Title = $"{DocumentName} — {ApplicationName}";

        DocumentStatistics statistics = DocumentStatistics.Of(document);
        bool onlyShapes = statistics.BrepShapes > 0 && statistics.Meshes == 0;
        List<string> summary = [];
        if (!onlyShapes)
        {
            summary.Add($"{Formatting.Count(statistics.Meshes, "mesh", "meshes")} " +
                $"({Formatting.Count(statistics.PlacedMeshes)} placed)");
            summary.Add(Formatting.Count(statistics.PlacedTriangles, "triangle", "triangles"));
        }
        if (statistics.BrepShapes > 0)
        {
            summary.Add($"{Formatting.Count(statistics.BrepShapes, "B-rep shape", "B-rep shapes")} " +
                $"({Formatting.Count(statistics.PlacedBrepShapes)} placed)");
        }
        summary.Add(Formatting.Count(statistics.Materials, "material", "materials"));
        summary.Add("mm");
        summary.Add("Z up");
        Summary = string.Join(" · ", summary);

        ModelTree.Show(document);
        Materials.Show(document);
    }

    private void Write(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            IDocumentWriter writer = _formats.WriterFor(path);
            writer.Write(Document!, path);
            _path = path;
            DocumentName = name;
            Title = $"{name} — {ApplicationName}";
            StatusMessage = $"Saved {name} ({writer.FormatName}).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = $"Cannot save {name}: {exception.Message}";
        }
    }

    private bool IsWritable(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return _formats.Writers.Any(writer => writer.Extensions.Contains(extension));
    }

    private static bool IsExpected(Exception exception) => exception is IOException or InvalidDataException
        or NotSupportedException or UnauthorizedAccessException or GeometryKernelException;
}
