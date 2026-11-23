using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Meshes;
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
        Properties.Animations = Animations;
        Properties.Refused = message => StatusMessage = message;
        Properties.PlaceAnchor = PlaceAnchor;
        Properties.LookFrom = row => LookFrom(row.Anchor, row.Path);
        ModelTree.Edit = Edit;
        Pose = Animations.Pose;
        Animations.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnimationsViewModel.Pose))
            {
                UpdatePose();
                UpdateBeacons();
            }
        };
        Properties.Picker.ChooseImage = () => FileDialogs?.OpenAsync(
            "Choose an image for the material", [new FileType("Images", [".png", ".jpg", ".jpeg", ".webp"])])
            ?? Task.FromResult<string?>(null);
        ModelTree.EnterCommand = EnterNodeCommand;
        _toolHost = new ToolHost(this);
        Tools = kernel is null
            ? [new ToolItemViewModel(new SelectTool(), "Icon.Select") { IsActive = true }, Interact()]
            :
            [
                new ToolItemViewModel(new SelectTool(kernel), "Icon.Select") { IsActive = true },
                Interact(),
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
                new ToolItemViewModel(new SimplifyTool(OpenSimplify), "Icon.Simplify"),
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
                SelectedInView = _selectingInView && ModelTree.SelectedNode is not null;
                ShowProperties();
                Animations.Filter(ModelTree.SelectedNode?.Path);
                OnEditingChanged();
                OnPropertyChanged(nameof(EmphasizedPath));
                ShowOverlay();
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

    /// <summary>The animations of the document, which the properties of what they move play and the 3D view shows.</summary>
    public AnimationsViewModel Animations { get; } = new();

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

    /// <summary>Whether a text field has the keyboard: the keys that delete then delete text, from the menu too.</summary>
    public bool IsTypingInField
    {
        get => _isTypingInField;
        set
        {
            if (SetProperty(ref _isTypingInField, value))
            {
                OnPropertyChanged(nameof(CanDelete));
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isTypingInField;

    /// <summary>Whether faces or edges are selected, which can be erased, or else an entity, which can be deleted.</summary>
    public bool CanDelete => Session is not null && !IsBusy && !IsTypingInField &&
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
    public IReadOnlyList<ElementReference> ShownElements => IsLookOnly ? [] : SelectedElements;

    /// <summary>Where the edits happen: the model, or the block of a reference entered; null without a document.</summary>
    public EditContext? Context { get; private set; }

    /// <summary>Whether a block is being edited rather than the model.</summary>
    public bool IsEditingBlock => Context is { IsModel: false };

    /// <summary>
    /// The entities that the 3D view shows in front of the rest, which it fades: the node selected, or else the reference
    /// whose block is being edited; null for none.
    /// </summary>
    public IReadOnlyList<Entity>? EmphasizedPath => IsLookOnly
        ? null
        : (FramedByBox ? null : ModelTree.SelectedNode?.Path) ??
          (Context is { IsModel: false } context ? context.Path : null);

    // Whether the node selected in the 3D view is a component, which it frames by its box and handles; a
    // mesh or a shape that was picked is shown in front of the rest instead, as one selected in the outline is.
    private bool FramedByBox => SelectedInView &&
        (ModelTree.SelectedNode is { Entity: BlockReference } || ActiveTool is SelectTool { IsPlacing: true });

    /// <summary>
    /// Whether the selected node was picked in the 3D view, which frames it by its box, rather than in the outline, which
    /// shows it in front of the rest.
    /// </summary>
    public bool SelectedInView { get; private set; }

    private bool _selectingInView;

    /// <summary>The references entered, by name, from the model: "Car › Front wheel".</summary>
    public string ContextName => Context is null
        ? string.Empty
        : string.Join(" › ", Context.Path.Select(reference => reference.Name ?? reference.Block.Name));

    /// <summary>
    /// The menu of a right click in the 3D view, for what is under the pointer: what is clicked in the block being
    /// edited is selected first, as the select tool would, and the menu offers what can be done with it; over nothing,
    /// what can be done with the view.
    /// </summary>
    public IReadOnlyList<ContextAction> ContextActions(ViewportPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        var actions = new List<ContextAction>();
        if (Session is null || IsBusy)
        {
            return actions;
        }
        bool editable = IsEditable;
        if (pick.Path is { Count: > 0 } path && editable && Context is { } context)
        {
            IReadOnlyList<Entity> within = LeaveTo(path);
            if (within.Count > 0)
            {
                IReadOnlyList<Entity> target = [.. path.Take(path.Count - within.Count + 1)];
                _selectingInView = true;
                try
                {
                    ModelTree.SelectCommand.Execute(target);
                }
                finally
                {
                    _selectingInView = false;
                }
                SelectedInView = ModelTree.SelectedNode is not null;
                ShowOverlay();
                Entity entity = target[^1];
                if (entity is BlockReference && ModelTree.SelectedNode is { } node)
                {
                    actions.Add(new("Edit block", EnterNodeCommand, node));
                }
                if (Animations.CanToggleAt(path, pick.Point))
                {
                    actions.Add(new("Interact", ToggleAtCommand, path));
                }
                actions.Add(ContextAction.Separator);
                foreach (ToolItemViewModel tool in Tools.Where(item => item.Tool is MoveTool or RotateTool or ScaleTool))
                {
                    actions.Add(new(tool.Name, ActivateToolCommand, tool));
                }
                if (SimplifiedComponent.MeshesOf(entity).Count > 0)
                {
                    actions.Add(new("Simplify…", SimplifyAtCommand, target));
                }
                actions.Add(ContextAction.Separator);
                actions.Add(new("Hide", ModelTree.ToggleVisibleCommand, ModelTree.SelectedNode));
                actions.Add(new("Copy", CopyCommand, Gesture: "⌘C"));
                actions.Add(new("Cut", CutCommand, Gesture: "⌘X"));
                actions.Add(new("Delete", DeleteCommand, Gesture: "⌫"));
                actions.Add(ContextAction.Separator);
            }
        }
        else if (pick.Path is { Count: > 0 } walked && Animations.CanToggleAt(walked, pick.Point))
        {
            actions.Add(new("Interact", ToggleAtCommand, walked));
            actions.Add(ContextAction.Separator);
        }
        if (editable && CanPaste)
        {
            actions.Add(new("Paste", PasteCommand, Gesture: "⌘V"));
        }
        if (Context is { IsModel: false })
        {
            actions.Add(new("Leave block", LeaveContextCommand, Gesture: "Esc"));
        }
        actions.Add(new("Fit all", FitAllCommand));
        if (editable)
        {
            actions.Add(new("Import…", ImportCommand, Gesture: "⇧⌘I"));
        }
        // No separator first, last, or twice.
        var tidy = new List<ContextAction>();
        foreach (ContextAction action in actions)
        {
            if (!action.IsSeparator || tidy.Count > 0 && !tidy[^1].IsSeparator)
            {
                tidy.Add(action);
            }
        }
        if (tidy.Count > 0 && tidy[^1].IsSeparator)
        {
            tidy.RemoveAt(tidy.Count - 1);
        }
        return tidy;
    }

    [RelayCommand]
    private void ToggleAt(IReadOnlyList<Entity>? path) => Animations.ToggleAt(path);

    /// <summary>Of the eyes above the point clicked, for a point of view added there: those of a person sitting.</summary>
    internal const double SeatedEyeHeight = 750;

    /// <summary>An anchor to add to the component at the end of a path, at a point of the model clicked on it.</summary>
    public sealed record AnchorRequest(IReadOnlyList<Entity> Target, Point3d Point, AnchorKind Kind);

    // The transform of a reference as the 3D view shows it: moved by the animations, or as in the document.
    private Transform Shown(BlockReference reference) =>
        Animations.Pose.TryGetValue(reference, out Transform? posed) ? posed : reference.Transform;

    // From the coordinates of the block of the reference at the end of a path to those of the model, as shown.
    private Transform PlacementOf(IEnumerable<Entity> path) =>
        path.OfType<BlockReference>().Aggregate(Transform.Identity, (all, reference) => all * Shown(reference));

    // An anchor in the block of the component, at the point clicked, facing the way the camera looks at it, the Z axis
    // up; a point of view at the height of the eyes of a person sitting there.
    [RelayCommand]
    private void AddAnchor(AnchorRequest request)
    {
        if (request.Target is not [.., BlockReference component])
        {
            return;
        }
        Transform toBlock = PlacementOf(request.Target).Inverse();
        bool view = request.Kind == AnchorKind.PointOfView;
        Point3d at = view ? request.Point + new Vector3d(0, 0, SeatedEyeHeight) : request.Point;
        Vector3d look = ViewEye is { } eye ? request.Point - eye : Vector3d.UnitX;
        look = new Vector3d(look.X, look.Y, 0);
        look = look.Length < 1e-9 ? Vector3d.UnitX : look.Normalized();
        Vector3d x = toBlock.ApplyVector(look).Normalized();
        Vector3d z = toBlock.ApplyVector(Vector3d.UnitZ);
        z = (z - x * z.Dot(x)).Normalized();
        Vector3d y = z.Cross(x);
        Point3d o = toBlock.Apply(at);
        var placement = new Transform(x.X, y.X, z.X, o.X, x.Y, y.Y, z.Y, o.Y, x.Z, y.Z, z.Z, o.Z);
        int count = component.Block.Entities.OfType<AnchorEntity>().Count(anchor => anchor.Kind == request.Kind);
        string name = (view ? "Point of view" : "Anchor") + (count > 0 ? $" {count + 1}" : string.Empty);
        if (Edit(new AddEntityCommand(view ? "Add point of view" : "Add anchor", component.Block,
                new AnchorEntity(placement, request.Kind) { Name = name })))
        {
            StatusMessage = view
                ? $"{name} added to {component.Name ?? component.Block.Name}: click it while walking to look from there."
                : $"{name} added to {component.Name ?? component.Block.Name}.";
        }
    }

    // The anchor or point of view to add by the next click in the 3D view, on the component at the end of the path.
    private (AnchorKind Kind, IReadOnlyList<Entity> Target)? _placingAnchor;

    /// <summary>Whether the next click in the 3D view places an anchor or a point of view on the component selected.</summary>
    public bool IsPlacingAnchor => _placingAnchor is not null;

    // Begins to place an anchor or a point of view on the component selected; Escape cancels.
    private void PlaceAnchor(AnchorKind kind)
    {
        if (ModelTree.SelectedNode is not { Entity: BlockReference, Path: { } path } || !IsEditable)
        {
            return;
        }
        _placingAnchor = (kind, path);
        StatusMessage = PlacingHint(_placingAnchor.Value);
    }

    private static string PlacingHint((AnchorKind Kind, IReadOnlyList<Entity> Target) placing)
    {
        string name = placing.Target[^1] is BlockReference component ? component.Name ?? component.Block.Name : "the component";
        return placing.Kind == AnchorKind.PointOfView
            ? $"Click on {name} where to look from: the eyes are above the point clicked. Escape cancels."
            : $"Click on {name} where to put the anchor. Escape cancels.";
    }

    // Looks from a point of view of a component, walking: from where the walk is, or from below it.
    private void LookFrom(AnchorEntity anchor, IReadOnlyList<Entity> path)
    {
        BlockReference[] chain = [.. path.OfType<BlockReference>()];
        if (!IsWalking)
        {
            Point3d eye = PlacementOf(chain).Apply(anchor.Point);
            double floor = Document?.GetBoundingBox() is { IsEmpty: false } box ? box.Min.Z : 0;
            StartWalkAt(new Point3d(eye.X, eye.Y, floor));
        }
        SitAt((anchor, chain));
    }

    private static readonly Avalonia.Media.Color AnchorColor = Avalonia.Media.Color.FromRgb(0xE8, 0x8A, 0x1A);

    // The anchors of the component selected, at any depth, where the 3D view shows them: rings for those to attach to,
    // crosses for the points of view.
    private IEnumerable<OverlayMarker> AnchorMarkers()
    {
        if (IsLookOnly || ModelTree.SelectedNode is not { Entity: BlockReference selected, Path: { } path })
        {
            yield break;
        }
        var pending = new Stack<(Block Block, Transform Placement, int Depth)>();
        pending.Push((selected.Block, PlacementOf(path), 0));
        while (pending.Count > 0)
        {
            (Block block, Transform placement, int depth) = pending.Pop();
            foreach (Entity entity in block.Entities)
            {
                if (entity is AnchorEntity anchor)
                {
                    yield return new OverlayMarker(placement.Apply(anchor.Point), AnchorColor,
                        anchor.Kind == AnchorKind.PointOfView ? MarkerShape.Plus : MarkerShape.Circle);
                }
                else if (entity is BlockReference reference && depth < 16)
                {
                    pending.Push((reference.Block, placement * Shown(reference), depth + 1));
                }
            }
        }
    }

    [RelayCommand]
    private void SimplifyAt(IReadOnlyList<Entity>? path)
    {
        if (path is { Count: > 0 })
        {
            OpenSimplify(path);
        }
    }

    /// <summary>Shows the simplification of a component, in a window of its own; set by the main window.</summary>
    public Func<SimplifyViewModel, Task>? ShowSimplify { get; set; }

    // Opens the simplification of the component or the mesh at the end of a path.
    private void OpenSimplify(IReadOnlyList<Entity> path)
    {
        if (ShowSimplify is not { } show || path.Count == 0 || IsBusy)
        {
            return;
        }
        Entity entity = path[^1];
        if (SimplifiedComponent.MeshesOf(entity).Count == 0)
        {
            StatusMessage = "It has no meshes to simplify.";
            return;
        }
        string name = entity.Name ?? (entity as BlockReference)?.Block.Name ?? (entity as MeshEntity)?.Material?.Name ?? "the mesh";
        _ = show(new SimplifyViewModel(entity, name, Edit));
    }

    // The tool that opens and closes what is clicked, as the rendered view and the walk do.
    private ToolItemViewModel Interact() =>
        new(new InteractTool((path, point) => Animations.CanToggleAt(path, point), (path, point) => Animations.ToggleAt(path, point)), "Icon.Interact");

    /// <summary>The tools of the tool column.</summary>
    public IReadOnlyList<ToolItemViewModel> Tools { get; }

    /// <summary>The tool that the input of the 3D view goes to; it works once a document is open.</summary>
    public Tool ActiveTool => Tools.Single(item => item.IsActive).Tool;

    /// <summary>What the active tool expects next.</summary>
    public string ToolHint => ActiveTool.IsActive && IsEditable ? ActiveTool.Hint : string.Empty;

    /// <summary>
    /// Passes from the move tool to the rotate tool or back, the point the selection was taken by kept, to turn around
    /// or move from; whether the active tool was one of them.
    /// </summary>
    public bool SwitchMoveRotate()
    {
        if (!IsEditable || IsBusy || ActiveTool is not TransformTool { IsActive: true } from || from is not (MoveTool or RotateTool) ||
            Tools.FirstOrDefault(item => item.Tool is MoveTool or RotateTool && item.Tool.GetType() != from.GetType())
                is not { } other)
        {
            return false;
        }
        Point3d? taken = from.TakenAt;
        ActivateTool(other);
        if (taken is { } point)
        {
            ((TransformTool)other.Tool).Grab(new Inference(InferenceKind.Endpoint, point));
        }
        ShowOverlay();
        return true;
    }

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
        ShowOverlay();
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
        if (_placingAnchor is { } placing)
        {
            if (input.Kind == ViewportInputKind.Click)
            {
                // On the component it is for, where it was clicked.
                if (input.Path is { } path && path.Count > placing.Target.Count &&
                    path.Take(placing.Target.Count).SequenceEqual(placing.Target))
                {
                    _placingAnchor = null;
                    AddAnchor(new AnchorRequest(placing.Target, input.Point, placing.Kind));
                }
                else
                {
                    StatusMessage = PlacingHint(placing);
                }
            }
            return;
        }
        if (IsPlacingWalk)
        {
            if (input.Kind == ViewportInputKind.Click && input.Path is not null)
            {
                // On a wall, stands on the floor of the model; on a surface facing up, on it.
                double floor = Document?.GetBoundingBox() is { IsEmpty: false } box ? box.Min.Z : 0;
                // The face as seen from where the click was made, whichever way its triangle is wound.
                System.Numerics.Vector3 normal = input.Ray is { } ray &&
                    input.Normal.X * ray.Direction.X + input.Normal.Y * ray.Direction.Y + input.Normal.Z * ray.Direction.Z > 0
                    ? -input.Normal : input.Normal;
                StartWalkAt(normal.Z > 0.5f ? input.Point : BeforeWall(input.Point, normal, floor));
            }
            return;
        }
        if (IsWalking || IsRendered)
        {
            if (input.Kind != ViewportInputKind.Click || IsBusy)
            {
                return;
            }
            // Sitting, a click on the seat stands up; what opens and closes still does.
            if (Seat is not null && PointOfViewAt(input.Path, input.Point) is { } same && same.Anchor == _seatAnchor)
            {
                Seat = null;
            }
            else if (Animations.ToggleAt(input.Path, input.Point))
            {
                UpdateBeacons(); // what was opened is no longer marked
            }
            else if (IsWalking && PointOfViewAt(input.Path, input.Point) is { } view)
            {
                SitAt(view);
            }
            else if (Seat is not null)
            {
                Seat = null; // a click on nothing stands up, back where the walk was
            }
            else
            {
                StatusMessage = input.Path is null ? string.Empty : "Nothing there to interact with.";
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
            ToToolModifiers(input.Modifiers), input.ClickCount,
            input.Handle is { } point && point >= 0 && point < _handleIndices.Length ? _handleIndices[point] : null);
        switch (input.Kind)
        {
            case ViewportInputKind.Hover:
                _lastHover = toolInput;
                tool.Hover(toolInput);
                // The handles of the face pointed at.
                if (tool is SelectTool { Face: var face } && face != _shownFace)
                {
                    ShowOverlay();
                }
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
        if (_placingAnchor is not null)
        {
            _placingAnchor = null;
            StatusMessage = string.Empty;
            return;
        }
        if (Seat is not null)
        {
            Seat = null;
            return;
        }
        if (IsWalking || IsPlacingWalk)
        {
            IsWalking = false;
            IsPlacingWalk = false;
            StatusMessage = string.Empty;
            return;
        }
        if (ActiveTool is { IsActive: true } tool && tool.Key(ToolKey.Escape, ToolModifiers.None))
        {
            return;
        }
        // What is shown in front of the rest, selected in the outline, goes back among it, still selected.
        if (ModelTree.SelectedNode is not null && !SelectedInView && !IsRendered)
        {
            SelectedInView = true;
            OnPropertyChanged(nameof(EmphasizedPath));
            ShowOverlay();
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

        // Selected in the 3D view: framed by its box, the rest as it is.
        public void SelectEntity(IReadOnlyList<Entity>? path)
        {
            owner._selectingInView = true;
            try
            {
                owner.ModelTree.SelectCommand.Execute(path);
            }
            finally
            {
                owner._selectingInView = false;
            }
        }

        public void ShowPreview(ToolPreview preview)
        {
            owner._preview = preview;
            owner.ShowOverlay();
        }

        public void PreviewPlacement(BlockReference reference, Transform? transform)
        {
            owner._toolPose = transform is null ? null : (reference, transform);
            owner.UpdatePose();
        }

        public void Report(string message) => owner.StatusMessage = message;
    }

    private (BlockReference Reference, Transform Transform)? _toolPose; // as a tool shows it while moving it

    /// <summary>
    /// The block references the 3D view shows placed otherwise than by their transformation: as the animations play,
    /// and as a tool moves them.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyDictionary<BlockReference, Transform>? Pose { get; private set; }

    private void UpdatePose()
    {
        if (_toolPose is not { } moved)
        {
            Pose = Animations.Pose;
            return;
        }
        var pose = new Dictionary<BlockReference, Transform>(Animations.Pose, ReferenceEqualityComparer.Instance)
        {
            [moved.Reference] = moved.Transform,
        };
        Pose = pose;
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

    /// <summary>
    /// Pastes what was cut or copied in the block being edited, as an imported model: it follows the pointer until a
    /// click places it, selected as in the 3D view; Escape takes it out again.
    /// </summary>
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
        StatusMessage = PlaceAdded(paste.Pasted, paste.Command)
            ? "Pasted. Click where it goes, or press Esc to leave it out."
            : "Pasted.";
    }

    // An entity just added by a command, selected as in the 3D view, follows the pointer by its origin, or the corner of
    // its box, until a click places it; Escape undoes the command. False when it cannot be placed so.
    private bool PlaceAdded(Entity added, IEditCommand command)
    {
        _toolHost.SelectEntity([.. Context!.Path, added]);
        if (!IsEditable || Tools.FirstOrDefault(item => item.Tool is SelectTool) is not { } select)
        {
            return false;
        }
        ActivateTool(select);
        Point3d anchor = added is BlockReference reference
            ? reference.Transform.Apply(Point3d.Origin)
            : added.GetBoundingBox().Min;
        return ((SelectTool)select.Tool).Place(Context.Placement.Apply(anchor), () =>
        {
            if (Session is { CanUndo: true } session && session.UndoName == command.Name && added.Owner is not null)
            {
                UndoCommand.Execute(null);
            }
        });
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
            ModelTree.DeleteCommand.Execute(ModelTree.SelectedNode); // with the others selected in the outline
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
            IsModified = Session.IsModified;
            ShowTitle();
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

    /// <summary>Whether the project has changes not saved, which a "*" after its name shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentLabel))]
    public partial bool IsModified { get; private set; }

    public string DocumentLabel => IsModified ? $"{DocumentName}*" : DocumentName;

    partial void OnDocumentNameChanged(string value) => OnPropertyChanged(nameof(DocumentLabel));

    private void ShowTitle() => Title = $"{DocumentLabel} — {ApplicationName}";

    /// <summary>Whether closing asks to save the changes; not in debug builds, quit and run again while developing.</summary>
    public bool AsksToSaveOnClose { get; set; }

    /// <summary>Asks whether to save the changes of the project named, before it is closed.</summary>
    public Func<string, Task<SaveChoice>>? AskToSave { get; set; }

    /// <summary>
    /// Whether the project can be closed: it has no changes, or they are saved or dropped as asked; false when the
    /// question is cancelled, or the project is not saved after all.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!AsksToSaveOnClose || !IsModified || AskToSave is not { } ask)
        {
            return true;
        }
        switch (await ask(DocumentName))
        {
            case SaveChoice.DontSave:
                return true;
            case SaveChoice.Save:
                await Save();
                return !IsModified;
            default:
                return false;
        }
    }

    /// <summary>What the document contains, for the status bar.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; }

    /// <summary>Outcome of the last operation, for the status bar.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; }

    /// <summary>Tells an error that nothing handled, in the status bar.</summary>
    public void ReportError(string message) => StatusMessage = message;

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

    /// <summary>Whether the view is only to look at, rendered or walked through: what is selected is not shown then.</summary>
    private bool IsLookOnly => IsRendered || IsWalkActive;

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
        (float)Cloudiness, GroundColor);

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
    [NotifyPropertyChangedFor(nameof(ToolHint), nameof(IsEditable), nameof(IsWalkActive), nameof(EmphasizedPath),
        nameof(ShownElements))]
    public partial bool IsWalking { get; set; }

    partial void OnIsWalkingChanged(bool value)
    {
        ShowOverlay(); // the box of the selection goes, and comes back
        if (!value)
        {
            Seat = null;
        }
        UpdateBeacons();
    }

    /// <summary>The point of view taken while walking, as sitting on a chair; null while walking on the floor.</summary>
    [ObservableProperty]
    public partial WalkSeat? Seat { get; set; }

    private AnchorEntity? _seatAnchor; // of the point of view taken

    partial void OnSeatChanged(WalkSeat? value)
    {
        if (value is null && _seatAnchor is not null)
        {
            _seatAnchor = null;
            StatusMessage = string.Empty; // the hint to stand up
        }
        UpdateBeacons();
    }

    /// <summary>Whether, while walking, a yellow dot marks what can be used: what opens or closes, where to sit.</summary>
    [ObservableProperty]
    public partial bool ShowsUsable { get; set; }

    partial void OnShowsUsableChanged(bool value) => UpdateBeacons();

    /// <summary>What can be used, which the 3D view marks while walking: see <see cref="ShowsUsable"/>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WalkBeacon> Beacons { get; private set; } = [];

    // What opens or closes, at its start, and the points of view not taken.
    private void UpdateBeacons()
    {
        if (!ShowsUsable || Document is null)
        {
            if (Beacons.Count > 0)
            {
                Beacons = [];
            }
            return;
        }
        var beacons = new List<WalkBeacon>(Animations.Beacons());
        // Where to sit is of the walk.
        foreach ((AnchorEntity anchor, BlockReference[] chain) in IsWalking ? ViewPoints() : [])
        {
            if (anchor != _seatAnchor)
            {
                // Over the seat, below the eyes.
                Point3d eye = PlacementOf(chain).Apply(anchor.Point);
                beacons.Add(new WalkBeacon(eye - new Vector3d(0, 0, SeatedEyeHeight / 2), 300));
            }
        }
        Beacons = beacons;
    }

    private List<(AnchorEntity Anchor, BlockReference[] Chain)>? _viewPoints;

    // The points of view of the document, each with the references from the model to the one holding it, once for every
    // place it is in; found again after a change of the document.
    private List<(AnchorEntity Anchor, BlockReference[] Chain)> ViewPoints()
    {
        if (_viewPoints is not null)
        {
            return _viewPoints;
        }
        var found = new List<(AnchorEntity, BlockReference[])>();
        var chain = new List<BlockReference>();
        void Walk(Block block)
        {
            foreach (Entity entity in block.Entities)
            {
                if (entity is AnchorEntity { Kind: AnchorKind.PointOfView } anchor)
                {
                    found.Add((anchor, [.. chain]));
                }
                else if (entity is BlockReference reference && chain.Count < 64)
                {
                    chain.Add(reference);
                    Walk(reference.Block);
                    chain.RemoveAt(chain.Count - 1);
                }
            }
        }
        if (Document is { } document)
        {
            Walk(document.Root);
        }
        return _viewPoints = found;
    }

    /// <summary>How far from the click a point of view of what was clicked may be, in millimetres.</summary>
    private const double ViewPointReach = 1500;

    // The point of view of what was clicked: among those inside the components of the path, the innermost, then the
    // nearest to the point clicked, within reach.
    private (AnchorEntity Anchor, BlockReference[] Chain)? PointOfViewAt(IReadOnlyList<Entity>? path, Point3d point)
    {
        if (path is null || ViewPoints() is not { Count: > 0 } all)
        {
            return null;
        }
        BlockReference[] references = [.. path.OfType<BlockReference>()];
        (AnchorEntity, BlockReference[])? best = null;
        int bestDepth = 0;
        double bestDistance = double.MaxValue;
        foreach ((AnchorEntity anchor, BlockReference[] chain) in all)
        {
            int depth = 0;
            while (depth < references.Length && depth < chain.Length && references[depth] == chain[depth])
            {
                depth++;
            }
            if (depth < AnimationsViewModel.Owning(chain) || depth < bestDepth)
            {
                continue;
            }
            Point3d eye = PlacementOf(chain).Apply(anchor.Point);
            // Across the floor: the eyes are above what is clicked.
            double distance = Math.Sqrt((eye.X - point.X) * (eye.X - point.X) + (eye.Y - point.Y) * (eye.Y - point.Y));
            if (distance <= ViewPointReach && (depth > bestDepth || distance < bestDistance))
            {
                best = (anchor, chain);
                bestDepth = depth;
                bestDistance = distance;
            }
        }
        return best;
    }

    // Looks from a point of view: the eyes at its origin, the way its X axis faces across the floor.
    private void SitAt((AnchorEntity Anchor, BlockReference[] Chain) view)
    {
        Transform frame = PlacementOf(view.Chain) * view.Anchor.Placement;
        Vector3d ahead = frame.ApplyVector(Vector3d.UnitX);
        _seatAnchor = view.Anchor;
        Seat = new WalkSeat(frame.Apply(Point3d.Origin), Math.Atan2(ahead.Y, ahead.X));
        StatusMessage = $"{view.Anchor.Name ?? "Point of view"}: click or press Escape to stand up.";
    }

    /// <summary>
    /// Whether the next click in the 3D view chooses where to stand, to walk from there: the pointer is a person with a
    /// cross under it. The inspector closes when the walk begins, to see more of the view.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWalkActive), nameof(EmphasizedPath), nameof(ShownElements))]
    public partial bool IsPlacingWalk { get; set; }

    partial void OnIsPlacingWalkChanged(bool value) => ShowOverlay();

    /// <summary>Where the walk begins, on the floor; null for the middle of what the camera looks at.</summary>
    [ObservableProperty]
    public partial Point3d? WalkStart { get; private set; }

    /// <summary>Whether the walk is on or waits for the place to stand.</summary>
    public bool IsWalkActive => IsWalking || IsPlacingWalk;

    [RelayCommand]
    private void ToggleWalk()
    {
        if (IsWalking || IsPlacingWalk)
        {
            IsWalking = false;
            IsPlacingWalk = false;
            return;
        }
        IsPlacingWalk = true;
        StatusMessage = "Click where to stand; Escape cancels.";
    }

    // On the floor of the model, in front of the wall that was clicked, on the side it faces, rather than in it.
    private Point3d BeforeWall(Point3d point, System.Numerics.Vector3 normal, double floor)
    {
        double x = normal.X, y = normal.Y, length = Math.Sqrt(x * x + y * y);
        double away = length > 1e-6 ? WalkSettings.CollisionRadius / length : 0;
        return new Point3d(point.X + x * away, point.Y + y * away, floor);
    }

    private void StartWalkAt(Point3d point)
    {
        WalkStart = point;
        IsPlacingWalk = false;
        IsInspectorOpen = false;
        StatusMessage = string.Empty;
        IsWalking = true;
    }

    /// <summary>Whether the grid on the ground and the axes through the origin are shown.</summary>
    [ObservableProperty]
    public partial bool IsGridVisible { get; set; }

    /// <summary>Whether the 3D view tints what is under the pointer; what is found there is used either way.</summary>
    [ObservableProperty]
    public partial bool HighlightsHover { get; set; }

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
            HighlightsHover = settings.HoverHighlight;
            TimeOfDay = Math.Clamp(settings.TimeOfDay, 0, 24);
            Latitude = Math.Clamp(settings.Latitude, -90, 90);
            NorthAngle = settings.NorthAngle;
            SunDate = settings.SunDate?.ToDateTime(TimeOnly.MinValue);
            Cloudiness = Math.Clamp(settings.Cloudiness, 0, 1);
            GroundColor = Avalonia.Media.Color.TryParse(settings.GroundColor, out Avalonia.Media.Color ground)
                ? Avalonia.Media.Color.FromRgb(ground.R, ground.G, ground.B) : ViewEnvironment.Lawn;
            EdgeWidth = Math.Clamp(settings.EdgeWidth, 0.25, 10);
            OutlineWidth = Math.Clamp(settings.OutlineWidth, 0.25, 10);
            Collisions = settings.Collisions;
            Gravity = settings.Gravity;
            ShowsUsable = settings.ShowUsable;
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
                or nameof(HighlightsHover)
                or nameof(TimeOfDay) or nameof(Latitude) or nameof(NorthAngle) or nameof(Collisions) or nameof(Gravity) or nameof(ShowsUsable)
                or nameof(SunDate) or nameof(Cloudiness) or nameof(GroundColor) or nameof(EdgeWidth) or nameof(OutlineWidth)
                or nameof(CollisionRadius) or nameof(EyeHeight) or nameof(WalkSpeed) or nameof(RunFactor)
                or nameof(WalkFieldOfView) or nameof(LookSensitivity)))
        {
            return;
        }
        _settings = _settings with
        {
            UseGlb = UseGlb, DarkTheme = IsDark, RenderStyle = RenderStyle, DisplayMode = DisplayMode,
            Perspective = IsPerspective, Grid = IsGridVisible, HoverHighlight = HighlightsHover, TimeOfDay = TimeOfDay, Latitude = Latitude,
            NorthAngle = NorthAngle, Collisions = Collisions, Gravity = Gravity, ShowUsable = ShowsUsable, SunDate = SunDate is { } day ? DateOnly.FromDateTime(day) : null,
            Cloudiness = Cloudiness, GroundColor = $"#{GroundColor.R:X2}{GroundColor.G:X2}{GroundColor.B:X2}",
            EdgeWidth = EdgeWidth, OutlineWidth = OutlineWidth, CollisionRadius = CollisionRadius,
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

    /// <summary>Whether walking follows the ground: it falls off an edge and rises over a low step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WalkSettings))]
    public partial bool Gravity { get; set; }

    /// <summary>The day of the sun in the rendered display mode; null for today.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial DateTime? SunDate { get; set; }

    /// <summary>The cover of clouds in the rendered display mode, from 0, a clear sky, to 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial double Cloudiness { get; set; }

    /// <summary>The colour of the ground in the rendered display mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkyEnvironment))]
    public partial Avalonia.Media.Color GroundColor { get; set; }

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
        EyeHeight, WalkSpeed, RunFactor, WalkFieldOfView, LookSensitivity * Math.PI / 180, CollisionRadius,
        Gravity);

    // The initial value is applied by the application, once Avalonia is ready.
    partial void OnIsDarkChanged(bool value)
    {
        if (_initialized)
        {
            ApplyAppearance();
        }
    }

    /// <summary>
    /// Starts an empty model, to draw in, saved under a name chosen when it is first saved; the changes of the one open
    /// are saved first, or dropped, as asked.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task New()
    {
        if (await ConfirmCloseAsync())
        {
            StartEmpty();
        }
    }

    private void StartEmpty()
    {
        var document = new Document();
        Show(document, DocumentStatistics.Of(document), null);
        StatusMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task Open()
    {
        if (FileDialogs is null || !await ConfirmCloseAsync())
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
            StartEmpty();
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
                // Placed at the origin, it is then moved where it goes.
                bool placing = PlaceAdded(command.Reference, command);
                string place = placing ? " Click where it goes, or press Esc to leave it out." : "";
                StatusMessage = warnings.Count switch
                {
                    0 => $"Imported {name} ({reader.FormatName}).{place}",
                    _ => $"Imported {name} ({reader.FormatName}), with {warnings.Count} warning(s): {warnings[0]}{place}",
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
        ShowOverlay(); // the box of the selection, moved
        Animations.Refresh();
        _viewPoints = null;
        UpdateBeacons();
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
        IsModified = false;
        ShowTitle();

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
        IsPlacingWalk = false;
        Properties.Know(document, statistics);
        _viewPoints = null;
        Animations.Show(document);
        ModelTree.Show(document);
        ShowProperties(); // of the model, nothing being selected
        Document = document;
        UpdateBeacons();
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
        ViewOverlay overlay = ToolOverlay.Of(_preview, IsDark, RenderStyle == RenderStyle.Cad, guides);
        Overlay = SelectionBox() is { } box ? overlay with { Lines = [.. overlay.Lines, .. box] } : overlay;
        if (AnchorMarkers().ToList() is { Count: > 0 } anchors)
        {
            Overlay = Overlay with { Markers = [.. Overlay.Markers, .. anchors] };
        }
        // The points to take the selection by, with the move and rotate tools, shown so as not to be looked for: those
        // the box does not hide.
        IReadOnlyList<int>? seen = null;
        if (SelectedInView && IsEditable && ActiveTool is TransformTool { IsActive: true } transform &&
            transform.GripPoints is { Count: > 0 } grips && SelectedHandles() is { } selectedBox)
        {
            seen = Seen(selectedBox);
            Overlay = Overlay with
            {
                // Squares move, dots turn: all the points of the box alike.
                Markers = [.. seen.Where(i => i < grips.Count).Select(i => new OverlayMarker(grips[i].Point,
                    SelectionBoxColor, transform is RotateTool ? MarkerShape.Dot : MarkerShape.Square)), .. Overlay.Markers],
            };
        }
        if (FocusAxes() is { } axes)
        {
            Overlay = Overlay with { Lines = [.. Overlay.Lines, .. axes] };
        }
        // With the select tool, the handles of the face pointed at: squares move, the arrow lying on it turns.
        SelectionHandles? handles = ShownHandles();
        _shownFace = (ActiveTool as SelectTool)?.Face;
        var points = new List<Point3d>();
        var indices = new List<int>();
        if (handles is not null)
        {
            seen = SeenToSelect(handles);
            var markers = new List<OverlayMarker>();
            var lines = new List<OverlayLine>();
            foreach (int i in seen)
            {
                SelectionHandle handle = handles.Handles[i];
                points.Add(handle.Point);
                indices.Add(i);
                if (handle.Kind != HandleKind.Rotate)
                {
                    markers.Add(new OverlayMarker(handle.Point, SelectionBoxColor, ShapeOf(handle.Grip.Kind)));
                    continue;
                }
                IReadOnlyList<IReadOnlyList<Point3d>> arrow = handles.TurnArrow(handle, ViewEye);
                lines.AddRange(arrow.Select(stroke => new OverlayLine(stroke, SelectionBoxColor, 2.5)));
                // The arrow is taken anywhere along it.
                foreach (Point3d along in arrow.Count > 0 ? arrow[0] : [])
                {
                    points.Add(along);
                    indices.Add(i);
                }
            }
            Overlay = Overlay with { Lines = [.. Overlay.Lines, .. lines], Markers = [.. Overlay.Markers, .. markers] };
        }
        Handles = points;
        _handleIndices = [.. indices];
        _seenHandles = SeenKey();
    }

    // What the camera sees of the handles shown, to show them again only when it changes.
    private string? SeenKey()
    {
        if (ViewEye is not { } eye)
        {
            return null;
        }
        if (ShownHandles() is { } handles)
        {
            return SeenKey(handles, SeenToSelect(handles), eye);
        }
        return ActiveTool is TransformTool && SelectedHandles() is { } box ? SeenKey(box, Seen(box), eye) : null;
    }

    // What is seen of the handles: which ones, and the size of the arrows to turn.
    private static string SeenKey(SelectionHandles handles, IReadOnlyList<int> seen, Point3d eye) => string.Join(",", seen
        .Select(i => handles.Handles[i] is { Kind: HandleKind.Rotate } turn
            ? $"{i}:{SelectionHandles.SightStep(turn.Point, eye)}"
            : $"{i}"));

    private static MarkerShape ShapeOf(InferenceKind kind) => kind switch
    {
        InferenceKind.Midpoint => MarkerShape.Diamond,
        _ => MarkerShape.Square,
    };

    private int? _shownFace; // the face of the box whose handles the select tool shows

    // The handles of the select tool: those of the face pointed at, none off the box; all those seen of a flat box,
    // which has no faces to point at.
    private IReadOnlyList<int> SeenToSelect(SelectionHandles handles) =>
        (ActiveTool as SelectTool)?.Face is { } face ? handles.OnFace(face)
        : handles.IsFlat ? Seen(handles)
        : [];

    // The handles the box of the selection does not hide from the camera; all of them before the view has drawn.
    private IReadOnlyList<int> Seen(SelectionHandles handles) =>
        ViewEye is { } eye ? handles.VisibleFrom(eye) : [.. Enumerable.Range(0, handles.Handles.Count)];

    // The box of the selection in the 3D view, for the move and rotate tools.
    private SelectionHandles? SelectedHandles()
    {
        if (Context is not { } context || ModelTree.SelectedNode?.Path is not { } path ||
            context.Reach(path) is not var (entity, placement))
        {
            return null;
        }
        if (_selectedHandles is not { } made || !ReferenceEquals(made.Entity, entity) ||
            _selectedHandlesFor != (placement, Session?.Revision ?? 0))
        {
            _selectedHandles = new SelectionHandles(entity, placement);
            _selectedHandlesFor = (placement, Session?.Revision ?? 0);
        }
        return _selectedHandles;
    }

    private SelectionHandles? _selectedHandles;
    private (Transform, int)? _selectedHandlesFor;
    private int[] _handleIndices = []; // for each point of Handles, the index of the handle of the select tool
    private string? _seenHandles; // which handles were shown, to show them again only when the camera changes that

    /// <summary>Where the camera of the 3D view is, as of its last frame.</summary>
    public Point3d? ViewEye
    {
        get => _viewEye;
        set
        {
            if (_viewEye == value)
            {
                return;
            }
            _viewEye = value;
            // Turning the view shows other handles only when it shows other faces of the box, or nears the arrows.
            if (value is not null && SeenKey() is { } seen && seen != _seenHandles)
            {
                ShowOverlay();
            }
        }
    }

    private Point3d? _viewEye;

    /// <summary>The points of the handles of the selection, which the 3D view lets drag.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Point3d> Handles { get; private set; } = [];

    // The handles of the entity selected in the 3D view, when the select tool can drag them.
    private SelectionHandles? ShownHandles() =>
        FramedByBox && IsEditable && ActiveTool is SelectTool { IsActive: true, IsDraggingHandle: false } select
            ? select.Handles
            : null;

    // The axes of the reference shown in front of the rest, from its origin, their positive halves only: red X, green
    // Y, blue Z, a quarter of the diagonal of its box long.
    private List<OverlayLine>? FocusAxes()
    {
        if (EmphasizedPath is not [.., BlockReference focused] path)
        {
            return null;
        }
        Transform placement = path.OfType<BlockReference>().Aggregate(Transform.Identity,
            (current, reference) => current * reference.Transform);
        BoundingBox box = focused.Block.GetBoundingBox();
        double length = box.IsEmpty ? 100 : Math.Max(100, 0.25 * box.Min.DistanceTo(box.Max));
        Point3d origin = placement.Apply(Point3d.Origin);
        return
        [
            new OverlayLine([origin, placement.Apply(new Point3d(length, 0, 0))], Avalonia.Media.Color.FromRgb(0xD8, 0x32, 0x2A), 2),
            new OverlayLine([origin, placement.Apply(new Point3d(0, length, 0))], Avalonia.Media.Color.FromRgb(0x2E, 0x9E, 0x44), 2),
            new OverlayLine([origin, placement.Apply(new Point3d(0, 0, length))], Avalonia.Media.Color.FromRgb(0x2F, 0x6F, 0xDE), 2),
        ];
    }

    private static readonly Avalonia.Media.Color SelectionBoxColor = Avalonia.Media.Color.FromRgb(0x2F, 0x7B, 0xF5);

    // The box of the entity selected in the 3D view, turned with it: its twelve edges.
    private List<OverlayLine>? SelectionBox()
    {
        if (!FramedByBox || IsLookOnly || ActiveTool is SelectTool { IsDraggingHandle: true } ||
            ModelTree.SelectedNode is not { Entity: { } entity, Path: { } path })
        {
            return null;
        }
        Transform placement = path.Take(path.Count - 1).OfType<BlockReference>()
            .Aggregate(Transform.Identity, (current, reference) => current * reference.Transform);
        BoundingBox box;
        if (entity is BlockReference selected)
        {
            box = selected.Block.GetBoundingBox();
            placement *= selected.Transform;
        }
        else
        {
            box = entity.GetBoundingBox();
        }
        if (box.IsEmpty)
        {
            return null;
        }
        Point3d Corner(int i) => placement.Apply(new Point3d(
            (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z));
        int[][] edges = [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];
        return [.. edges.Select(edge => new OverlayLine([Corner(edge[0]), Corner(edge[1])], SelectionBoxColor, 1.5))];
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
            Session?.MarkSaved();
            IsModified = false;
            ShowTitle();
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
