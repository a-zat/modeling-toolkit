using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>State and commands of the main window, shared by both layouts.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string ApplicationName = "Modeling Toolkit";

    private readonly DocumentFormats _formats;
    private readonly IAppearance _appearance;
    private readonly bool _initialized;
    private Document? _document;
    private string? _path;

    public MainWindowViewModel(DocumentFormats formats, IAppearance appearance)
    {
        _formats = formats;
        _appearance = appearance;

        Title = ApplicationName;
        DocumentName = "No document";
        Summary = "Open a model with File › Open";
        StatusMessage = string.Empty;
        IsModelPanelVisible = true;
        IsPropertiesPanelVisible = true;
        ActivePanel = SidePanel.Model;
        Layout = LayoutKind.Classic;
        Theme = ThemeKind.Fluent;
        IsDark = true;
        IsCompactDensity = true;

        ModelTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModelTreeViewModel.SelectedNode))
            {
                Properties.Show(ModelTree.SelectedNode);
            }
        };
        Palette.Commands =
        [
            new("Open…", OpenCommand, Gesture: "Ctrl+O"),
            new("Save", SaveCommand, Gesture: "Ctrl+S"),
            new("Save As…", SaveAsCommand, Gesture: "Ctrl+Shift+S"),
            new("Fit All", NotAvailableCommand, "Fit all"),
            new("Standard Views", NotAvailableCommand, "Standard views"),
            new("Shaded", NotAvailableCommand, "Shaded"),
            new("Shaded with Edges", NotAvailableCommand, "Shaded with edges"),
            new("Wireframe", NotAvailableCommand, "Wireframe"),
            new("Layout A: Classic", SetLayoutCommand, LayoutKind.Classic),
            new("Layout B: Compact", SetLayoutCommand, LayoutKind.Compact),
            new("Toggle Dark Theme", ToggleDarkCommand),
        ];
        _initialized = true;
    }

    public ModelTreeViewModel ModelTree { get; } = new();

    public PropertiesViewModel Properties { get; } = new();

    public MaterialsViewModel Materials { get; } = new();

    public CommandPaletteViewModel Palette { get; } = new();

    /// <summary>The open and save dialogs, provided by the window.</summary>
    public IFileDialogs? FileDialogs { get; set; }

    public bool HasDocument => _document is not null;

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

    // Layout A: both side panels can be hidden.

    [ObservableProperty]
    public partial bool IsModelPanelVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPropertiesPanelVisible { get; set; }

    // Layout B: one side panel at a time.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidePanelOpen), nameof(IsModelPanelActive), nameof(IsMaterialsPanelActive))]
    public partial SidePanel ActivePanel { get; set; }

    public bool IsSidePanelOpen => ActivePanel != SidePanel.None;

    public bool IsModelPanelActive => ActivePanel == SidePanel.Model;

    public bool IsMaterialsPanelActive => ActivePanel == SidePanel.Materials;

    // Prototype (#10): layout and theme to compare.

    public IReadOnlyList<LayoutKind> Layouts { get; } = Enum.GetValues<LayoutKind>();

    public IReadOnlyList<ThemeKind> Themes { get; } = Enum.GetValues<ThemeKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassicLayout), nameof(IsCompactLayout))]
    public partial LayoutKind Layout { get; set; }

    public bool IsClassicLayout => Layout == LayoutKind.Classic;

    public bool IsCompactLayout => Layout == LayoutKind.Compact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDensity))]
    public partial ThemeKind Theme { get; set; }

    /// <summary>Only the Fluent theme has a compact density.</summary>
    public bool HasDensity => Theme == ThemeKind.Fluent;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    [ObservableProperty]
    public partial bool IsCompactDensity { get; set; }

    /// <summary>Applies the theme, the variant and the density chosen here.</summary>
    public void ApplyAppearance() => _appearance.Apply(Theme, IsDark, IsCompactDensity);

    /// <summary>Reads a model and shows it; failures are reported in the status bar.</summary>
    public async Task LoadAsync(string path)
    {
        string name = Path.GetFileName(path);
        StatusMessage = $"Opening {name}…";
        try
        {
            IDocumentReader reader = _formats.ReaderFor(path);
            Document document = await Task.Run(() => reader.Read(path));
            Show(document, path);
            StatusMessage = $"Opened {name} ({reader.FormatName}).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = $"Cannot open {name}: {exception.Message}";
        }
    }

    partial void OnThemeChanged(ThemeKind value) => ApplyAppearanceOnceInitialized();

    partial void OnIsDarkChanged(bool value) => ApplyAppearanceOnceInitialized();

    partial void OnIsCompactDensityChanged(bool value) => ApplyAppearanceOnceInitialized();

    // The initial values are applied by the application, once Avalonia is ready.
    private void ApplyAppearanceOnceInitialized()
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

    // The commands of the 3D viewport only report that they are not available yet.
    [RelayCommand]
    private void NotAvailable(string feature) =>
        StatusMessage = $"{feature}: available once the 3D viewport is in place (#13).";

    [RelayCommand]
    private void SetLayout(LayoutKind layout) => Layout = layout;

    [RelayCommand]
    private void SetTheme(ThemeKind theme) => Theme = theme;

    [RelayCommand]
    private void ToggleDark() => IsDark = !IsDark;

    [RelayCommand]
    private void ToggleModelPanel() => IsModelPanelVisible = !IsModelPanelVisible;

    [RelayCommand]
    private void TogglePropertiesPanel() => IsPropertiesPanelVisible = !IsPropertiesPanelVisible;

    // Selecting the active panel again closes it, as in VS Code.
    [RelayCommand]
    private void ShowPanel(SidePanel panel) => ActivePanel = ActivePanel == panel ? SidePanel.None : panel;

    private void Show(Document document, string path)
    {
        _document = document;
        _path = path;
        DocumentName = Path.GetFileName(path);
        Title = $"{DocumentName} — {ApplicationName}";

        DocumentStatistics statistics = DocumentStatistics.Of(document);
        Summary = $"{Formatting.Count(statistics.Meshes)} meshes ({Formatting.Count(statistics.PlacedMeshes)} placed) · " +
            $"{Formatting.Count(statistics.PlacedTriangles)} triangles · {Formatting.Count(statistics.Materials)} materials · " +
            "mm · Z up";

        ModelTree.Show(document);
        Materials.Show(document);
        OnPropertyChanged(nameof(HasDocument));
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    private void Write(string path)
    {
        string name = Path.GetFileName(path);
        try
        {
            IDocumentWriter writer = _formats.WriterFor(path);
            writer.Write(_document!, path);
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
