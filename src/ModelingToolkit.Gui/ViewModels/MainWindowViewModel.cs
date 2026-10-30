using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>State and commands of the main window.</summary>
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
        ActivePanel = SidePanel.Model;
        IsDark = true;

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

    // One side panel at a time, chosen in the activity bar.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSidePanelOpen), nameof(IsModelPanelActive), nameof(IsMaterialsPanelActive))]
    public partial SidePanel ActivePanel { get; set; }

    public bool IsSidePanelOpen => ActivePanel != SidePanel.None;

    public bool IsModelPanelActive => ActivePanel == SidePanel.Model;

    public bool IsMaterialsPanelActive => ActivePanel == SidePanel.Materials;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

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
            Document document = await Task.Run(() => reader.Read(path));
            Show(document, path);
            StatusMessage = $"Opened {name} ({reader.FormatName}).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = $"Cannot open {name}: {exception.Message}";
        }
    }

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

    // The commands of the 3D viewport only report that they are not available yet.
    [RelayCommand]
    private void NotAvailable(string feature) =>
        StatusMessage = $"{feature}: available once the 3D viewport is in place (#13).";

    [RelayCommand]
    private void ToggleDark() => IsDark = !IsDark;

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
