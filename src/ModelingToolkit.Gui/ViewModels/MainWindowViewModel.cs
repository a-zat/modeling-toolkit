using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
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
    private Action? _cancelOperation; // of the file being opened or saved, whose method owns its source
    private bool _saving;
    private TaskCompletionSource _idle = Completed();

    /// <param name="kernel">The kernel the drawing tools work with; without one, only the selection tool is offered.</param>
    /// <param name="settings">Where the settings are kept; in memory without one.</param>
    public MainWindowViewModel(DocumentFormats formats, IAppearance appearance, IGeometryKernel? kernel = null,
        ISettingsStore? settings = null)
    {
        _formats = formats;
        _appearance = appearance;
        _settingsStore = settings ?? new MemorySettingsStore();

        Title = ApplicationName;
        DocumentName = "No document";
        Summary = "Start a model with File › New, or open one with File › Open";
        StatusMessage = string.Empty;
        IsInspectorOpen = true;
        IsOutlineExpanded = true;
        // The application starts in a view to edit with, never rendered.
        AppSettings saved = _settingsStore.Load();
        ApplySettings(saved.DisplayMode == DisplayMode.Rendered ? saved with { DisplayMode = DisplayMode.ShadedWithEdges } : saved);
        _settingsStore.Changed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySettings(_settingsStore.Load()));

        Properties.Edit = Edit;
        Layers.Edit = Edit;
        Properties.Picker.ChooseImage = () => FileDialogs?.OpenAsync(
            "Choose an image for the material", [new FileType("Images", [".png", ".jpg", ".jpeg", ".webp"])])
            ?? Task.FromResult<string?>(null);
        ModelTree.EnterCommand = EnterNodeCommand;
        _toolHost = new ToolHost(this);
        Tools = kernel is null
            ? [new ToolItemViewModel(new SelectTool(), "Icon.Select") { IsActive = true }]
            :
            [
                new ToolItemViewModel(new SelectTool(), "Icon.Select") { IsActive = true },
                new ToolItemViewModel(new LineTool(kernel), "Icon.Line"),
                new ToolItemViewModel(new RectangleTool(kernel), "Icon.Rectangle"),
                new ToolItemViewModel(new CircleTool(kernel), "Icon.Circle"),
                new ToolItemViewModel(new ArcTool(kernel), "Icon.Arc"),
                new ToolItemViewModel(new PushPullTool(kernel), "Icon.PushPull"),
                new ToolItemViewModel(new OffsetTool(kernel), "Icon.Offset"),
                new ToolItemViewModel(new MoveTool(kernel), "Icon.Move"),
                new ToolItemViewModel(new RotateTool(kernel), "Icon.Rotate"),
                new ToolItemViewModel(new ScaleTool(kernel), "Icon.Scale"),
                new ToolItemViewModel(new PaintTool(), "Icon.Paint"),
                new ToolItemViewModel(new TapeTool(kernel), "Icon.Tape"),
            ];
        ActiveTool.HintChanged += OnToolHintChanged;
        ActiveTool.MeasurementChanged += OnToolMeasurementChanged;
        if (PaintTool is { } paint)
        {
            PaintPicker.Chosen = material => paint.Material = material;
            paint.MaterialChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(PaintName));
                OnPropertyChanged(nameof(PaintBrush));
            };
        }
        ModelTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModelTreeViewModel.SelectedNode))
            {
                // A node and elements are not selected together; a node outside the context leaves it.
                if (ModelTree.SelectedNode is { } node)
                {
                    Elements?.Clear();
                    LeaveTo(node.Path);
                }
                ShowProperties();
                Animations.Filter(ModelTree.SelectedNode?.Path);
                OnEditingChanged();
                OnPropertyChanged(nameof(EmphasizedPath));
            }
        };
        Palette.Commands =
        [
            new("Open…", OpenCommand, Gesture: Shortcut("O")),
            new("Save", SaveCommand, Gesture: Shortcut("S")),
            new("Save As…", SaveAsCommand, Gesture: Shortcut("S", shift: true)),
            new("Undo", UndoCommand, Gesture: Shortcut("Z")),
            new("Redo", RedoCommand, Gesture: OperatingSystem.IsMacOS() ? Shortcut("Z", shift: true) : Shortcut("Y")),
            new("Cut", CutCommand, Gesture: Shortcut("X")),
            new("Copy", CopyCommand, Gesture: Shortcut("C")),
            new("Paste", PasteCommand, Gesture: Shortcut("V")),
            new("Delete", DeleteCommand),
            new("Delete Guides", DeleteGuidesCommand),
            new("Walk", ToggleWalkCommand),
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
            new("Rendered", SetDisplayModeCommand, DisplayMode.Rendered),
            new("Standard Rendering", SetRenderStyleCommand, RenderStyle.Standard),
            new("CAD Rendering", SetRenderStyleCommand, RenderStyle.Cad),
            new("Toggle Perspective", TogglePerspectiveCommand),
            new("Toggle Grid", ToggleGridCommand),
            new("Toggle Dark Theme", ToggleDarkCommand),
            new("Show or Hide the Inspector", ToggleInspectorCommand),
            new("Settings", OpenSettingsCommand),
        ];
        _initialized = true;
    }

    public ModelTreeViewModel ModelTree { get; } = new();

    public PropertiesViewModel Properties { get; } = new();

    /// <summary>The animations of the document, which the inspector plays and the 3D view shows.</summary>
    public AnimationsViewModel Animations { get; } = new();

    /// <summary>The layers of the document, shown or hidden in the 3D view.</summary>
    public LayersViewModel Layers { get; } = new();

    /// <summary>What the lower part of the inspector shows: "Properties", "Layers" or "Animations".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPropertiesTab), nameof(IsLayersTab), nameof(IsAnimationsTab))]
    public partial string InspectorTab { get; set; } = "Properties";

    public bool IsPropertiesTab => InspectorTab == "Properties";

    public bool IsLayersTab => InspectorTab == "Layers";

    public bool IsAnimationsTab => InspectorTab == "Animations";

    [RelayCommand]
    private void ShowInspectorTab(string tab) => InspectorTab = tab;

    [RelayCommand]
    private void ShowAllLayers() => Layers.SetAllVisible(true);

    public CommandPaletteViewModel Palette { get; } = new();

    private PaintTool? PaintTool => Tools.Select(item => item.Tool).OfType<PaintTool>().FirstOrDefault();

    /// <summary>Whether the select tool is active, with which a drag holding Command selects in a window.</summary>
    public bool IsSelecting => ActiveTool is SelectTool;

    /// <summary>The references to the block being edited; empty for the model.</summary>
    public IReadOnlyList<Entity> EditPath => Context?.Path ?? [];

    /// <summary>Selects the faces and edges of a window, in place of the selection or after it.</summary>
    [RelayCommand]
    private void WindowSelect(WindowSelection selection)
    {
        if (Elements is null || IsBusy)
        {
            return;
        }
        ModelTree.SelectedNode = null;
        Elements.Select(selection.Elements, selection.Add);
    }

    /// <summary>Whether the paint tool is active, which shows its material in the status bar.</summary>
    public bool IsPainting => ActiveTool is PaintTool;

    /// <summary>The choice of the material of the paint tool.</summary>
    public MaterialPickerViewModel PaintPicker { get; } = new();

    public string PaintName => PaintTool?.Material?.Name ?? "Default";

    public Avalonia.Media.IBrush PaintBrush => MaterialSwatch.BrushOf(PaintTool?.Material);

    [RelayCommand]
    private void OpenPaintPicker() => PaintPicker.Open(Document);

    /// <summary>Runs the commands that change the open document, and undoes them; null without a document.</summary>
    public EditSession? Session { get; private set; }

    public bool CanUndo => Session?.CanUndo == true && !IsBusy;

    public bool CanRedo => Session?.CanRedo == true && !IsBusy;

    /// <summary>The tooltip of the undo button, with the name of the command it undoes.</summary>
    public string UndoTip => $"Undo{(CanUndo ? " " + Session!.UndoName : "")} ({Shortcut("Z")})";

    public string RedoTip =>
        $"Redo{(CanRedo ? " " + Session!.RedoName : "")} ({(OperatingSystem.IsMacOS() ? Shortcut("Z", shift: true) : Shortcut("Y"))})";

    private readonly EntityClipboard _clipboard = new();

    /// <summary>Whether an entity is selected, which can be cut or copied.</summary>
    public bool CanCopy => Session is not null && !IsBusy && ModelTree.SelectedNode?.Entity is not null;

    /// <summary>Whether an entity was cut or copied in the open document, which can be pasted.</summary>
    public bool CanPaste => Session is not null && !IsBusy && _clipboard.CanPaste(Session.Document);

    /// <summary>Whether faces or edges are selected, which can be erased, or else an entity, which can be deleted.</summary>
    public bool CanDelete => Session is not null && !IsBusy &&
        (SelectedElements.Any(element => element.Kind != ElementKind.Vertex) || ModelTree.SelectedNode?.Entity is not null);

    /// <summary>
    /// Runs a command on the open document; a command that refuses its change reports why in the status bar. Nothing
    /// is changed while a file is being opened or saved, which reads the document on another thread.
    /// </summary>
    public bool Edit(IEditCommand command)
    {
        if (Session is null || IsBusy)
        {
            return false;
        }

        try
        {
            Session.Execute(command);
            return true;
        }
        catch (EditRefusedException exception)
        {
            StatusMessage = exception.Message;
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Session!.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        try
        {
            Session!.Redo();
        }
        catch (EditRefusedException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    /// <summary>The faces, edges and vertices selected in the open document; null without a document.</summary>
    public ElementSelection? Elements { get; private set; }

    /// <summary>The elements of <see cref="Elements"/>, which the 3D view highlights.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownElements))]
    public partial IReadOnlyList<ElementReference> SelectedElements { get; private set; } = [];

    /// <summary>The elements that the 3D view highlights: those selected, but in the rendered display mode.</summary>
    public IReadOnlyList<ElementReference> ShownElements => IsRendered ? [] : SelectedElements;

    /// <summary>Where the edits happen: the model, or the block of a reference entered; null without a document.</summary>
    public EditContext? Context { get; private set; }

    /// <summary>Whether a block is being edited rather than the model.</summary>
    public bool IsEditingBlock => Context is { IsModel: false };

    /// <summary>
    /// The entities that the 3D view shows in front of the rest, which it fades: the node selected, or else the reference
    /// whose block is being edited; null for none.
    /// </summary>
    public IReadOnlyList<Entity>? EmphasizedPath => IsRendered
        ? null
        : ModelTree.SelectedNode?.Path ?? (Context is { IsModel: false } context ? context.Path : null);

    /// <summary>The references entered, by name, from the model: "Car › Front wheel".</summary>
    public string ContextName => Context is null
        ? string.Empty
        : string.Join(" › ", Context.Path.Select(reference => reference.Name ?? reference.Block.Name));

    /// <summary>The tools of the tool column.</summary>
    public IReadOnlyList<ToolItemViewModel> Tools { get; }

    /// <summary>The tool that the input of the 3D view goes to; it works once a document is open.</summary>
    public Tool ActiveTool => Tools.Single(item => item.IsActive).Tool;

    /// <summary>What the active tool expects next.</summary>
    public string ToolHint => ActiveTool.IsActive && IsEditable ? ActiveTool.Hint : string.Empty;

    [RelayCommand]
    private void ActivateTool(ToolItemViewModel item)
    {
        Tool previous = ActiveTool;
        if (ReferenceEquals(item.Tool, previous) || !IsEditable)
        {
            return;
        }

        previous.Deactivate();
        previous.HintChanged -= OnToolHintChanged;
        previous.MeasurementChanged -= OnToolMeasurementChanged;
        foreach (ToolItemViewModel tool in Tools)
        {
            tool.IsActive = ReferenceEquals(tool, item);
        }
        item.Tool.HintChanged += OnToolHintChanged;
        item.Tool.MeasurementChanged += OnToolMeasurementChanged;
        if (Session is not null)
        {
            item.Tool.Activate(_toolHost);
        }
        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(IsPainting));
        OnPropertyChanged(nameof(IsSelecting));
        OnPropertyChanged(nameof(ToolHint));
        OnToolMeasurementChanged(this, EventArgs.Empty);
    }

    private void OnToolHintChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ToolHint));

    private void OnToolMeasurementChanged(object? sender, EventArgs e)
    {
        Measurement = ActiveTool.IsActive ? ActiveTool.Measurement ?? string.Empty : string.Empty;
        MeasurementName = ActiveTool.MeasurementName.ToUpperInvariant();
        OnPropertyChanged(nameof(HasMeasurement));
    }

    /// <summary>
    /// The measure of what the active tool is doing, such as the length of the line being drawn, which a value typed in
    /// its place changes: see <see cref="TypeMeasurementCommand"/>.
    /// </summary>
    [ObservableProperty]
    public partial string Measurement { get; set; } = string.Empty;

    /// <summary>What the measure is, as the label of the field shows it: "LENGTH", "RADIUS".</summary>
    [ObservableProperty]
    public partial string MeasurementName { get; private set; } = "LENGTH";

    /// <summary>Whether the active tool takes typed values now.</summary>
    public bool HasMeasurement => ActiveTool.IsActive && ActiveTool.Measurement is not null;

    /// <summary>Gives the active tool a value typed in the measurement field, such as the length of a line.</summary>
    [RelayCommand]
    private void TypeMeasurement(string? text)
    {
        if (ActiveTool.IsActive && !IsBusy && IsEditable && !ActiveTool.TypeMeasurement(text ?? string.Empty))
        {
            StatusMessage = $"\"{text}\" is not a measure that the {ActiveTool.Name.ToLowerInvariant()} tool takes now.";
            OnToolMeasurementChanged(this, EventArgs.Empty); // shows the measure again
        }
    }

    /// <summary>What the active tool shows over the 3D view.</summary>
    [ObservableProperty]
    public partial ViewOverlay Overlay { get; private set; } = ViewOverlay.Empty;

    /// <summary>Passes a key of the 3D view, such as an arrow that locks an axis, to the active tool.</summary>
    [RelayCommand]
    private void PressToolKey(ToolKey key)
    {
        if (ActiveTool.IsActive && !IsBusy && IsEditable)
        {
            ActiveTool.Key(key, ToolModifiers.None);
        }
    }

    /// <summary>Passes an input of the pointer in the 3D view to the active tool.</summary>
    [RelayCommand]
    private void ViewInput(ViewportPick input)
    {
        if (IsWalking || IsRendered)
        {
            if (input.Kind == ViewportInputKind.Click && !IsBusy && !Animations.ToggleAt(input.Path))
            {
                StatusMessage = input.Path is null ? string.Empty : "Nothing there opens or closes.";
            }
            return;
        }
        Tool tool = ActiveTool;
        if (!tool.IsActive || IsBusy)
        {
            return;
        }

        var toolInput = new ToolInput(
            new ToolPick(input.Path, input.Element, input.Point, new Vector3d(input.Normal.X, input.Normal.Y, input.Normal.Z),
                input.Ray),
            ToToolModifiers(input.Modifiers), input.ClickCount);
        switch (input.Kind)
        {
            case ViewportInputKind.Hover:
                _lastHover = toolInput;
                tool.Hover(toolInput);
                break;
            case ViewportInputKind.Press:
                tool.Press(toolInput);
                break;
            case ViewportInputKind.Drag:
                tool.Drag(toolInput);
                break;
            case ViewportInputKind.Release:
                tool.Release(toolInput);
                break;
            default:
                tool.Click(toolInput);
                break;
        }
    }

    /// <summary>
    /// Escape closes the settings when they are open; otherwise it goes to the active tool, and leaves the block being
    /// edited if the tool does not take it.
    /// </summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsSettingsOpen)
        {
            IsSettingsOpen = false;
            return;
        }
        if (IsWalking)
        {
            IsWalking = false;
            return;
        }
        if (ActiveTool is { IsActive: true } tool && tool.Key(ToolKey.Escape, ToolModifiers.None))
        {
            return;
        }
        LeaveContext();
    }

    private static ToolModifiers ToToolModifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? ToolModifiers.Shift : ToolModifiers.None) |
        (modifiers.HasFlag(KeyModifiers.Control) ? ToolModifiers.Control : ToolModifiers.None) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? ToolModifiers.Alt : ToolModifiers.None) |
        (modifiers.HasFlag(KeyModifiers.Meta) ? ToolModifiers.Meta : ToolModifiers.None);

    private readonly ToolHost _toolHost;
    private ToolInput? _lastHover; // what is under the pointer, for the tool to look at again when the context changes

    // What the tools work on: the open document, and the selection, whose entity is the node of the model tree.
    private sealed class ToolHost(MainWindowViewModel owner) : IToolHost
    {
        public EditSession Session => owner.Session!;

        public EditContext Context => owner.Context!;

        public ElementSelection Elements => owner.Elements!;

        public IReadOnlyList<Entity>? SelectedEntity => owner.ModelTree.SelectedNode?.Path;

        public void SelectEntity(IReadOnlyList<Entity>? path) => owner.ModelTree.SelectCommand.Execute(path);

        public void ShowPreview(ToolPreview preview)
        {
            owner._preview = preview;
            owner.ShowOverlay();
        }

        public void Report(string message) => owner.StatusMessage = message;
    }

    // Leaves the context until it holds the path of entities, and gives the path from the block being edited.
    private IReadOnlyList<Entity> LeaveTo(IReadOnlyList<Entity> path)
    {
        if (Context is null)
        {
            return path;
        }
        while (true)
        {
            if (Context.Within(path) is { } within)
            {
                return within;
            }
            if (Context.IsModel)
            {
                return []; // the model node
            }
            Context.Leave();
        }
    }

    /// <summary>Edits the block of the reference of a node of the model tree, from the model.</summary>
    [RelayCommand]
    private void EnterNode(ModelNodeViewModel? node)
    {
        if (Context is not { } context || node?.Entity is not BlockReference || IsBusy)
        {
            return;
        }

        ModelTree.SelectedNode = null;
        context.LeaveAll();
        foreach (Entity entity in node.Path)
        {
            context.Enter((BlockReference)entity);
        }
    }

    /// <summary>Goes back to the block the one being edited was entered from.</summary>
    [RelayCommand]
    private void LeaveContext()
    {
        if (Context is { IsModel: false } context)
        {
            ModelTree.SelectedNode = null;
            context.Leave();
        }
    }

    private void OnContextChanged(object? sender, EventArgs e)
    {
        Elements?.Clear();
        ShowOverlay();
        ModelTree.ShowContext(Context?.Path ?? []);
        if (_lastHover is { } hover && ActiveTool.IsActive)
        {
            ActiveTool.Hover(hover);
        }
        OnPropertyChanged(nameof(IsEditingBlock));
        OnPropertyChanged(nameof(ContextName));
        OnPropertyChanged(nameof(EditPath));
        OnPropertyChanged(nameof(EmphasizedPath));
    }

    private void OnElementsChanged(object? sender, EventArgs e)
    {
        SelectedElements = Elements!.Elements;
        ShowProperties();
        OnEditingChanged();
    }

    // The elements selected, or else the node, or else the model.
    private void ShowProperties(bool refresh = false)
    {
        if (SelectedElements.Count > 0)
        {
            Properties.ShowElements(SelectedElements);
        }
        else if (refresh)
        {
            Properties.Refresh(ModelTree.SelectedNode ?? ModelTree.Model);
        }
        else
        {
            Properties.Show(ModelTree.SelectedNode ?? ModelTree.Model);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        _clipboard.Copy(Session!.Document, ModelTree.SelectedNode!.Path);
        OnEditingChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Cut()
    {
        Entity entity = ModelTree.SelectedNode!.Entity!;
        _clipboard.Copy(Session!.Document, ModelTree.SelectedNode.Path);
        Edit(new CompositeCommand("Cut", [new DeleteCommand(entity)]));
        OnEditingChanged();
    }

    /// <summary>Pastes what was cut or copied where it was, in the block being edited, selected for the move tool.</summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboard.Paste(Session!.Document, Context!) is not { } paste)
        {
            StatusMessage = "A block cannot be pasted into itself.";
            return;
        }
        if (!Edit(paste.Command))
        {
            return;
        }
        ModelTree.SelectCommand.Execute((IReadOnlyList<Entity>)[.. Context!.Path, paste.Pasted]);
        if (Tools.FirstOrDefault(item => item.Tool is MoveTool) is { } move)
        {
            ActivateTool(move);
        }
    }

    /// <summary>Deletes the guides of the block being edited, as one command.</summary>
    [RelayCommand]
    private void DeleteGuides()
    {
        if (Context?.Block.Entities.OfType<GuideEntity>().ToArray() is { Length: > 0 } guides)
        {
            Edit(new CompositeCommand("Delete Guides", [.. guides.Select(guide => new DeleteCommand(guide))]));
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedElements.Count == 0)
        {
            Edit(new DeleteCommand(ModelTree.SelectedNode!.Entity!));
            return;
        }
        try
        {
            if (EraseCommand.Of(SelectedElements) is { } erase)
            {
                Edit(erase);
            }
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException)
        {
            StatusMessage = $"The selection could not be erased: {exception.Message}";
        }
    }

    // What can be edited, undone and redone follows the session, the selection and the file operations.
    private void OnEditingChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanPaste));
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        if (Session is not null)
        {
            Title = $"{(Session.IsModified ? "• " : "")}{DocumentName} — {ApplicationName}";
        }
    }

    partial void OnIsBusyChanged(bool value) => OnEditingChanged();

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
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand))]
    public partial Document? Document { get; private set; }

    public bool HasDocument => Document is not null;

    /// <summary>Whether a file is being opened or saved; the other file commands wait for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(SaveCommand), nameof(SaveAsCommand),
        nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    public bool CanSave => HasDocument && !IsBusy;

    /// <summary>The part done of the file being opened or saved, from 0 to 1.</summary>
    [ObservableProperty]
    public partial double OperationProgress { get; private set; }

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

    /// <summary>Whether the inspector is shown, on the right of the 3D view: the outline and the properties.</summary>
    [ObservableProperty]
    public partial bool IsInspectorOpen { get; set; }

    /// <summary>Whether the outline is open in the inspector, above the properties.</summary>
    [ObservableProperty]
    public partial bool IsOutlineExpanded { get; set; }

    /// <summary>Whether the settings are open, over the middle of the window.</summary>
    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [ObservableProperty]
    public partial bool IsDark { get; set; }

    // How the 3D view shows the document.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShaded), nameof(IsShadedWithEdges), nameof(IsWireframe), nameof(IsRendered),
        nameof(IsEditable), nameof(ToolHint), nameof(EmphasizedPath), nameof(ShownElements))]
    public partial DisplayMode DisplayMode { get; set; }

    /// <summary>
    /// Whether the view is rendered, under the sky at <see cref="TimeOfDay"/>: only to look at, the tools taking no input,
    /// nothing highlighted, a click opening or closing the part under the pointer that an animation moves.
    /// </summary>
    public bool IsRendered
    {
        get => DisplayMode == DisplayMode.Rendered;
        set => Choose(value, DisplayMode.Rendered, nameof(IsRendered));
    }

    /// <summary>Whether the tools edit the model through the 3D view: not when it is rendered, nor while walking.</summary>
    public bool IsEditable => !IsRendered && !IsWalking;

    // The tool at work stops when the view is only to look at, and starts again after.
    partial void OnDisplayModeChanged(DisplayMode oldValue, DisplayMode newValue)
    {
        // The settings set the mode before there are tools.
        if ((oldValue == DisplayMode.Rendered) == (newValue == DisplayMode.Rendered) ||
            Tools?.FirstOrDefault(item => item.IsActive)?.Tool is not { } tool)
        {
            return;
        }
        if (newValue == DisplayMode.Rendered)
        {
            tool.Deactivate();
            Overlay = ViewOverlay.Empty;
        }
        else if (Session is not null && !tool.IsActive)
        {
            tool.Activate(_toolHost);
        }
    }

    /// <summary>The sky and the sun of the rendered display mode, today at <see cref="TimeOfDay"/> where the model is.</summary>
    public ViewEnvironment SkyEnvironment => new(
        SunPosition.Direction(TimeOfDay, DateOnly.FromDateTime(SunDate ?? DateTime.Today), Latitude, NorthAngle),
        (float)Cloudiness);

    /// <summary>The hour shown by the slider of the rendered display mode: "14:30".</summary>
    public string TimeOfDayText => $"{(int)TimeOfDay:00}:{(int)Math.Round(TimeOfDay % 1 * 60) % 60:00}";

    // The display modes are those of a selector: one is always chosen. For its buttons, which turn themselves off when
    // clicked while chosen: the choice is set again, and the button shows it.

    public bool IsShaded
    {
        get => DisplayMode == DisplayMode.Shaded;
        set => Choose(value, DisplayMode.Shaded, nameof(IsShaded));
    }

    public bool IsShadedWithEdges
    {
        get => DisplayMode == DisplayMode.ShadedWithEdges;
        set => Choose(value, DisplayMode.ShadedWithEdges, nameof(IsShadedWithEdges));
    }

    public bool IsWireframe
    {
        get => DisplayMode == DisplayMode.Wireframe;
        set => Choose(value, DisplayMode.Wireframe, nameof(IsWireframe));
    }

    private void Choose(bool chosen, DisplayMode mode, string property)
    {
        if (chosen)
        {
            DisplayMode = mode;
        }
        else if (DisplayMode == mode)
        {
            // After the button has finished unchecking itself.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(property));
        }
    }

    /// <summary>How the 3D view shades the faces, chosen in the settings.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandardRenderStyle), nameof(IsCadRenderStyle))]
    public partial RenderStyle RenderStyle { get; set; }

    // For the radio buttons of the settings: unchecking one, as the other is checked, changes nothing.

    public bool IsStandardRenderStyle
    {
        get => RenderStyle == RenderStyle.Standard;
        set => SetRenderStyleIf(value, RenderStyle.Standard);
    }

    public bool IsCadRenderStyle
    {
        get => RenderStyle == RenderStyle.Cad;
        set => SetRenderStyleIf(value, RenderStyle.Cad);
    }

    private void SetRenderStyleIf(bool chosen, RenderStyle style)
    {
        if (chosen)
        {
            RenderStyle = style;
        }
    }

    /// <summary>Perspective, or orthographic (parallel) projection.</summary>
    [ObservableProperty]
    public partial bool IsPerspective { get; set; }

    /// <summary>
    /// Whether the 3D view walks through the model at the height of the eyes: the tools take no input then, and a click
    /// opens or closes the part under the pointer that an animation moves.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolHint), nameof(IsEditable))]
    public partial bool IsWalking { get; set; }

    [RelayCommand]
    private void ToggleWalk() => IsWalking = !IsWalking;

    /// <summary>Whether the grid on the ground and the axes through the origin are shown.</summary>
    [ObservableProperty]
    public partial bool IsGridVisible { get; set; }

    /// <summary>Applies the variant of the theme chosen here.</summary>
    public void ApplyAppearance() => _appearance.Apply(IsDark);

    /// <summary>
    /// Reads a model and shows it, with its progress in the status bar, where <see cref="CancelCommand"/> stops it;
    /// failures are reported there too.
    /// </summary>
    public async Task LoadAsync(string path)
    {
        string name = Path.GetFileName(path);
        StatusMessage = $"Opening {name}…";
        using CancellationTokenSource cancellation = BeginOperation(saving: false);
        try
        {
            IDocumentReader reader = _formats.ReaderFor(path);
            var warnings = new List<string>();
            IProgress<double> progress = OperationProgressReporter();
            // The statistics visit every placed vertex: they are counted with the reading, away from the interface.
            (Document document, DocumentStatistics statistics) = await Task.Run(() =>
            {
                Document read = reader.Read(path, warnings, progress, cancellation.Token);
                if (UseGlb && IsGltf(path))
                {
                    DocumentRoot.Unwrap(read);
                }
                return (read, DocumentStatistics.Of(read));
            }, cancellation.Token);
            Show(document, statistics, path);
            StatusMessage = warnings.Count switch
            {
                0 => $"Opened {name} ({reader.FormatName}).",
                1 => $"Opened {name} ({reader.FormatName}), with a warning: {warnings[0]}",
                _ => $"Opened {name} ({reader.FormatName}), with {warnings.Count} warnings: {warnings[0]}",
            };
            StatusDetails = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings) : null;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Opening {name} was cancelled.";
        }
        catch (Exception exception)
        {
            // Whatever goes wrong with a file is reported rather than closing the application, which has nothing else
            // to lose: a file can fail in more ways than the formats document.
            StatusMessage = $"Cannot open {name}: {exception.Message}";
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Stops the file being opened or saved.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cancelOperation?.Invoke();

    /// <summary>
    /// Completes once no file is being opened or saved, so that the window can close: a file being opened is
    /// cancelled, one being saved is written to the end, so that the saving is neither lost nor half done.
    /// </summary>
    public Task WhenIdleAsync()
    {
        if (!_saving)
        {
            _cancelOperation?.Invoke();
        }
        return _idle.Task;
    }

    private CancellationTokenSource BeginOperation(bool saving)
    {
        var operation = new CancellationTokenSource();
        _cancelOperation = operation.Cancel;
        _saving = saving;
        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OperationProgress = 0;
        IsBusy = true;
        return operation;
    }

    private void EndOperation()
    {
        _cancelOperation = null;
        IsBusy = false;
        _idle.TrySetResult();
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }

    // Reports on the thread of the interface, which Progress<T> captures, however the operation reports.
    private Progress<double> OperationProgressReporter() => new(done => OperationProgress = done);

    partial void OnStatusMessageChanged(string value) => StatusDetails = null;

    /// <summary>
    /// Adds to the status bar what the 3D view leaves out of the document, such as shapes that cannot be turned into
    /// triangles; the details list it all.
    /// </summary>
    [RelayCommand]
    private void ReportViewWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        string? details = StatusDetails;
        StatusMessage = warnings.Count == 1
            ? $"{StatusMessage} The view leaves something out: {warnings[0]}"
            : $"{StatusMessage} The view leaves out {warnings.Count} things: {warnings[0]}";
        StatusDetails = string.Join(Environment.NewLine, details is null ? warnings : [details, .. warnings]);
    }

    private readonly ISettingsStore _settingsStore;
    private AppSettings _settings = new();
    private bool _applyingSettings;

    /// <summary>Where the settings are kept, for the user to edit them; null for nowhere.</summary>
    public string? SettingsLocation => _settingsStore.Location;

    /// <summary>
    /// Whether files are opened and saved as glTF directly: Save writes the .glb opened, Save As offers .glb, and the
    /// model space of a glTF file opened is its root node, without the nodes that only wrap it.
    /// </summary>
    [ObservableProperty]
    public partial bool UseGlb { get; set; }

    private void ApplySettings(AppSettings settings)
    {
        _applyingSettings = true;
        try
        {
            _settings = settings;
            UseGlb = settings.UseGlb;
            IsDark = settings.DarkTheme;
            RenderStyle = settings.RenderStyle;
            DisplayMode = settings.DisplayMode;
            IsPerspective = settings.Perspective;
            IsGridVisible = settings.Grid;
            TimeOfDay = Math.Clamp(settings.TimeOfDay, 0, 24);
            Latitude = Math.Clamp(settings.Latitude, -90, 90);
            NorthAngle = settings.NorthAngle;
            SunDate = settings.SunDate?.ToDateTime(TimeOnly.MinValue);
            Cloudiness = Math.Clamp(settings.Cloudiness, 0, 1);
            EdgeWidth = Math.Clamp(settings.EdgeWidth, 0.25, 10);
            OutlineWidth = Math.Clamp(settings.OutlineWidth, 0.25, 10);
            Collisions = settings.Collisions;
            CollisionRadius = Math.Clamp(settings.CollisionRadius, 50, 1000);
            EyeHeight = Math.Clamp(settings.EyeHeight, 500, 3000);
            WalkSpeed = Math.Clamp(settings.WalkSpeed, 100, 10000);
            RunFactor = Math.Clamp(settings.RunFactor, 1, 10);
            WalkFieldOfView = Math.Clamp(settings.WalkFieldOfView, 10, 120);
            LookSensitivity = Math.Clamp(settings.LookSensitivity, 0.01, 2);
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    // The settings changed in the interface are written at once.
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_applyingSettings || _settingsStore is null || e.PropertyName is not (nameof(UseGlb) or nameof(IsDark)
                or nameof(RenderStyle) or nameof(DisplayMode) or nameof(IsPerspective) or nameof(IsGridVisible)
                or nameof(TimeOfDay) or nameof(Latitude) or nameof(NorthAngle) or nameof(Collisions)
                or nameof(SunDate) or nameof(Cloudiness) or nameof(EdgeWidth) or nameof(OutlineWidth)
                or nameof(CollisionRadius) or nameof(EyeHeight) or nameof(WalkSpeed) or nameof(RunFactor)
                or nameof(WalkFieldOfView) or nameof(LookSensitivity)))
        {
            return;
        }
        _settings = _settings with
        {
            UseGlb = UseGlb, DarkTheme = IsDark, RenderStyle = RenderStyle, DisplayMode = DisplayMode,
            Perspective = IsPerspective, Grid = IsGridVisible, TimeOfDay = TimeOfDay, Latitude = Latitude,
            NorthAngle = NorthAngle, Collisions = Collisions, SunDate = SunDate is { } day ? DateOnly.FromDateTime(day) : null,
            Cloudiness = Cloudiness, EdgeWidth = EdgeWidth, OutlineWidth = OutlineWidth, CollisionRadius = CollisionRadius,
            EyeHeight = EyeHeight, WalkSpeed = WalkSpeed, RunFactor = RunFactor, WalkFieldOfView = WalkFieldOfView,
            LookSensitivity = LookSensitivity,
        };
        _settingsStore.Save(_settings);
    }

    /// <summary>The hour of the day that the rendered display mode shows, from 0 to 24.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment), nameof(TimeOfDayText))]
    public partial double TimeOfDay { get; set; }

    /// <summary>Where the model is, in degrees north, for the course of the sun.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial double Latitude { get; set; }

    /// <summary>The direction of north from the Y axis of the model, in degrees counterclockwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial double NorthAngle { get; set; }

    /// <summary>Whether walking stops at walls and other parts of the model, sliding along them.</summary>
    [ObservableProperty]
    public partial bool Collisions { get; set; }

    /// <summary>The day of the sun in the rendered display mode; null for today.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial DateTime? SunDate { get; set; }

    /// <summary>The cover of clouds in the rendered display mode, from 0, a clear sky, to 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial double Cloudiness { get; set; }

    [RelayCommand]
    private void SetSunDateToday() => SunDate = null;

    /// <summary>The widths, in points, of the edges over the faces and of the outlines of the CAD render style.</summary>
    [ObservableProperty]
    public partial double EdgeWidth { get; set; }

    [ObservableProperty]
    public partial double OutlineWidth { get; set; }

    // How a person walks, in millimetres and seconds; the look sensitivity in degrees per point.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double CollisionRadius { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double EyeHeight { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double WalkSpeed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double RunFactor { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double WalkFieldOfView { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial double LookSensitivity { get; set; }

    /// <summary>How the 3D view lets a person walk.</summary>
    public WalkSettings WalkSettings => new(
        EyeHeight, WalkSpeed, RunFactor, WalkFieldOfView, LookSensitivity * Math.PI / 180, CollisionRadius);

    // The initial value is applied by the application, once Avalonia is ready.
    partial void OnIsDarkChanged(bool value)
    {
        if (_initialized)
        {
            ApplyAppearance();
        }
    }

    /// <summary>Starts an empty model, to draw in, saved under a name chosen when it is first saved.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void New()
    {
        var document = new Document();
        Show(document, DocumentStatistics.Of(document), null);
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
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

    /// <summary>
    /// Brings a model from a file into the open one, as a block placed at the origin of the block being edited, in
    /// place of opening it: see <see cref="ImportCommand"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task Import()
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
        string? path = await FileDialogs.OpenAsync("Import a model", types);
        if (path is not null)
        {
            await ImportAsync(path);
        }
    }

    /// <summary>Imports a model from a file into the open one, starting an empty one if there is none.</summary>
    public async Task ImportAsync(string path)
    {
        if (Session is null || Context is null || Document is null)
        {
            New();
        }

        string name = Path.GetFileName(path);
        StatusMessage = $"Importing {name}…";
        using CancellationTokenSource cancellation = BeginOperation(saving: false);
        try
        {
            IDocumentReader reader = _formats.ReaderFor(path);
            var warnings = new List<string>();
            IProgress<double> progress = OperationProgressReporter();
            Document imported = await Task.Run(() => reader.Read(path, warnings, progress, cancellation.Token),
                cancellation.Token);
            var command = new ImportCommand(Document!, Context!.Block, imported, Path.GetFileNameWithoutExtension(path));
            EndOperation(); // the edit is refused while busy
            if (Edit(command))
            {
                ModelTree.SelectCommand.Execute((IReadOnlyList<Entity>)[.. Context.Path, command.Reference]);
                StatusMessage = warnings.Count switch
                {
                    0 => $"Imported {name} ({reader.FormatName}).",
                    _ => $"Imported {name} ({reader.FormatName}), with {warnings.Count} warning(s): {warnings[0]}",
                };
                StatusDetails = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings) : null;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Importing {name} was cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Cannot import {name}: {exception.Message}";
        }
        finally
        {
            if (IsBusy)
            {
                EndOperation();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (_path is not null && IsWritable(_path))
        {
            await WriteAsync(_path);
        }
        else
        {
            await SaveAs();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAs()
    {
        if (FileDialogs is null)
        {
            return;
        }

        IDocumentWriter writer = SaveAsWriter;
        string suggestedName = Path.ChangeExtension(Path.GetFileName(_path) ?? "model", writer.Extensions[0]);
        string? path = await FileDialogs.SaveAsync(
            "Save the model", new FileType(writer.FormatName, writer.Extensions), suggestedName);
        if (path is not null)
        {
            await WriteAsync(path);
        }
    }

    [RelayCommand]
    private void FitAll() => Camera?.FitAll();

    [RelayCommand]
    private void ShowView(StandardView view) => Camera?.ShowView(view);

    [RelayCommand]
    private void SetDisplayMode(DisplayMode mode) => DisplayMode = mode;

    [RelayCommand]
    private void SetRenderStyle(RenderStyle style) => RenderStyle = style;

    [RelayCommand]
    private void TogglePerspective() => IsPerspective = !IsPerspective;

    [RelayCommand]
    private void ToggleGrid() => IsGridVisible = !IsGridVisible;

    [RelayCommand]
    private void ToggleDark() => IsDark = !IsDark;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

    [RelayCommand]
    private void ToggleOutline() => IsOutlineExpanded = !IsOutlineExpanded;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    // The changes of the document are those that subscribed before, the model tree first, already know.
    partial void OnDocumentChanged(Document? oldValue, Document? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Changed -= OnDocumentEdited;
        }
        if (newValue is not null)
        {
            newValue.Changed += OnDocumentEdited;
        }
    }

    private void OnDocumentEdited(object? sender, DocumentChange change)
    {
        ShowProperties(refresh: true);
        Animations.Refresh();
        Layers.Refresh();
        if (Document is { } document)
        {
            ShowSummary(DocumentStatistics.Of(document));
        }
    }

    // What the model holds, in the status bar.
    private void ShowSummary(DocumentStatistics statistics)
    {
        if (statistics.Meshes == 0 && statistics.BrepShapes == 0)
        {
            Summary = "Empty model · mm · Z up";
            return;
        }

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
    }


    private void Show(Document document, DocumentStatistics statistics, string? path)
    {
        _path = path;
        DocumentName = path is null ? "Untitled" : Path.GetFileName(path);
        Title = $"{DocumentName} — {ApplicationName}";

        ShowSummary(statistics);

        if (Session is not null)
        {
            Session.Changed -= OnSessionChanged;
        }
        Session = new EditSession(document);
        Session.Changed += OnSessionChanged;
        if (PaintTool is { } paint)
        {
            paint.Material = null; // a material of the document closed
        }
        if (Elements is not null)
        {
            Elements.Changed -= OnElementsChanged;
        }
        if (Context is not null)
        {
            Context.Changed -= OnContextChanged;
        }
        // Before the properties, which show the elements that remain after a change.
        Elements = new ElementSelection(document);
        Elements.Changed += OnElementsChanged;
        SelectedElements = [];
        Context = new EditContext(document);
        Context.Changed += OnContextChanged;
        _lastHover = null; // of the previous document
        OnContextChanged(this, EventArgs.Empty);
        // The tool starts again on the new document.
        ActiveTool.Deactivate();
        ActiveTool.Activate(_toolHost);
        OnPropertyChanged(nameof(ToolHint));
        OnToolMeasurementChanged(this, EventArgs.Empty);

        IsWalking = false; // the view turns to the new document
        Properties.Know(document, statistics);
        Animations.Show(document);
        Layers.Show(document);
        ModelTree.Show(document);
        ShowProperties(); // of the model, nothing being selected
        Document = document;
        OnEditingChanged();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        OnEditingChanged();
        ShowOverlay();
    }

    private ToolPreview _preview = ToolPreview.Empty;

    // What the tool shows, with the guides of the block being edited.
    private void ShowOverlay()
    {
        Transform placement = Context?.Placement ?? Transform.Identity;
        GuideLine[] guides = Context is null ? [] : [.. Context.Block.Entities.OfType<GuideEntity>()
            .Select(guide => new GuideLine(placement.Apply(guide.Point), placement.ApplyVector(guide.Direction).Normalized()))];
        Overlay = ToolOverlay.Of(_preview, IsDark, RenderStyle == RenderStyle.Cad, guides);
    }

    private async Task WriteAsync(string path)
    {
        string name = Path.GetFileName(path);
        StatusMessage = $"Saving {name}…";
        Document document = Document!;
        using CancellationTokenSource cancellation = BeginOperation(saving: true);
        try
        {
            IDocumentWriter writer = _formats.WriterFor(path);
            IProgress<double> progress = OperationProgressReporter();
            var warnings = new List<string>();
            await Task.Run(() =>
            {
                if (writer is Kernel.Occt.IO.GltfWriter gltf)
                {
                    gltf.Write(document, path, warnings, progress, cancellation.Token);
                }
                else
                {
                    writer.Write(document, path, progress, cancellation.Token);
                }
            }, cancellation.Token);
            _path = path;
            DocumentName = name;
            Title = $"{name} — {ApplicationName}";
            Session?.MarkSaved();
            StatusMessage = warnings.Count == 0
                ? $"Saved {name} ({writer.FormatName})."
                : $"Saved {name} ({writer.FormatName}), with {warnings.Count} warning(s): {warnings[0]}";
            StatusDetails = warnings.Count > 0 ? string.Join(Environment.NewLine, warnings) : null;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"Saving {name} was cancelled: the file is as it was.";
        }
        catch (Exception exception)
        {
            // As for opening; a failed writing leaves the file as it was, and the document is still open.
            StatusMessage = $"Cannot save {name}: {exception.Message} The file is as it was.";
        }
        finally
        {
            EndOperation();
        }
    }

    // A glTF file is saved as it is only in the glTF mode; otherwise, as before, in the native format.
    private bool IsWritable(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return (UseGlb || !IsGltf(path)) && _formats.Writers.Any(writer => writer.Extensions.Contains(extension));
    }

    private static bool IsGltf(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".glb" or ".gltf";

    // The writer of Save As: glTF in the glTF mode, the native format otherwise.
    private IDocumentWriter SaveAsWriter =>
        _formats.Writers.FirstOrDefault(writer => writer.Extensions.Contains(UseGlb ? ".glb" : ".mtk"))
        ?? _formats.Writers[0];
}
