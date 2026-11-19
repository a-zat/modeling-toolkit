using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// The structure of the document, as the outline shows it, and the selected node. The outline shows the children of
/// the model node, not the model node itself: the properties show the model when nothing is selected.
/// </summary>
public sealed partial class ModelTreeViewModel : ObservableObject
{
    /// <summary>The node of the model, above the nodes shown; null without a document.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Roots), nameof(TopNodes), nameof(IsEmpty))]
    public partial ModelNodeViewModel? Model { get; private set; }

    /// <summary>The nodes at the top of the outline: the references and the geometry of the model.</summary>
    public IReadOnlyList<ModelNodeViewModel> Roots => Model?.Children ?? [];

    /// <summary>The top of the outline: the model, named as its root block, such as the root node of a glTF file.</summary>
    public IReadOnlyList<ModelNodeViewModel> TopNodes => Model is null ? [] : [Model];

    [ObservableProperty]
    public partial ModelNodeViewModel? SelectedNode { get; set; }

    public bool IsEmpty => Model is null;

    /// <summary>Executed with a node to edit: one of a reference enters its block.</summary>
    public System.Windows.Input.ICommand? EnterCommand { get; set; }

    /// <summary>Runs the commands that rename, show or hide the entities; null when nothing can be edited.</summary>
    public Func<IEditCommand, bool>? Edit { get; set; }

    /// <summary>Shows a hidden entity, or hides a shown one, by a command that can be undone.</summary>
    [RelayCommand]
    private void ToggleVisible(ModelNodeViewModel? node)
    {
        if (node?.Entity is { } entity)
        {
            Edit?.Invoke(new SetHiddenCommand([entity], !entity.IsHidden));
        }
    }

    /// <summary>
    /// Whether the reference of a node can be dragged into the block of another: the project, a reference, or a
    /// geometry, which is of a block; not into its own block, nor into one it places.
    /// </summary>
    public bool CanMoveInto(ModelNodeViewModel dragged, ModelNodeViewModel target)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(target);
        return Edit is not null && dragged.Entity is BlockReference reference && target.Block is { } block &&
            !ReferenceEquals(reference.Owner, block) && !MoveToBlockCommand.Places(reference.Block, block);
    }

    /// <summary>Moves the reference of a node into the block of another, where it stays in the project.</summary>
    public bool MoveInto(ModelNodeViewModel dragged, ModelNodeViewModel target)
    {
        if (!CanMoveInto(dragged, target))
        {
            return false;
        }
        var reference = (BlockReference)dragged.Entity!;
        // A geometry has the path of the node it is the geometry of.
        IReadOnlyList<Entity> into = target.Path;
        var command = new MoveToBlockCommand(
            reference, Placement(dragged.Path.Take(dragged.Path.Count - 1)), target.Block!, Placement(into));
        if (Edit?.Invoke(command) != true)
        {
            return false;
        }
        Select([.. into, reference]);
        return true;
    }

    // From the coordinates of the block that a path of references leads to, to those of the project.
    private static Core.Geometry.Transform Placement(IEnumerable<Entity> path) =>
        path.OfType<BlockReference>().Aggregate(Core.Geometry.Transform.Identity,
            (placement, reference) => placement * reference.Transform);

    /// <summary>
    /// Adds a copy of the entity of a node next to it, in the same block and at the same place, selected; a copy of a
    /// reference places the same block, as a copy pasted does.
    /// </summary>
    [RelayCommand]
    private void Duplicate(ModelNodeViewModel? node)
    {
        if (node is not { Entity: { Owner: { } owner } entity, Path.Count: > 0 })
        {
            return;
        }
        Entity copy = TransformCommands.Duplicate(entity, Core.Geometry.Transform.Identity);
        copy.IsHidden = entity.IsHidden;
        if (Edit?.Invoke(new AddEntityCommand("Duplicate", owner, copy)) == true)
        {
            Select([.. node.Path.Take(node.Path.Count - 1), copy]);
        }
    }

    /// <summary>Moves a reference out of the block it is in, into the block that places that one, where it stays.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveOut))]
    private void MoveOut(ModelNodeViewModel? node)
    {
        if (!CanMoveOut(node) || Model?.Block is not { } root)
        {
            return;
        }
        var reference = (BlockReference)node!.Entity!;
        IReadOnlyList<Entity> into = [.. node.Path.Take(node.Path.Count - 2)];
        Block target = into.Count == 0 ? root : ((BlockReference)into[^1]).Block;
        var command = new MoveToBlockCommand(
            reference, Placement(node.Path.Take(node.Path.Count - 1)), target, Placement(into));
        if (Edit?.Invoke(command) == true)
        {
            Select([.. into, reference]);
        }
    }

    /// <summary>Whether a node is a reference in the block of another reference, which it can be moved out of.</summary>
    public static bool CanMoveOut(ModelNodeViewModel? node) =>
        node is { Entity: BlockReference, Path.Count: >= 2 } && node.Path[^2] is BlockReference;

    /// <summary>Starts editing the name of a node in the outline.</summary>
    /// <summary>Adds an empty block into the block of a node, the model or a reference, selected and named at once.</summary>
    [RelayCommand]
    private void AddBlock(ModelNodeViewModel? node)
    {
        if (node is not { CanHoldBlocks: true, Block: { } into } || Model?.Document is not { } document)
        {
            return;
        }
        var command = new NewBlockCommand(document, into, "New block");
        if (Edit?.Invoke(command) != true)
        {
            return;
        }
        Select([.. node.Path, command.Reference]);
        if (SelectedNode is { } added)
        {
            node.IsExpanded = true;
            StartRename(added);
        }
    }

    [RelayCommand]
    private void StartRename(ModelNodeViewModel? node)
    {
        if (node is { HasEntity: true } && Edit is not null)
        {
            node.EditedName = node.Name;
            node.IsRenaming = true;
        }
    }

    /// <summary>Gives the entity the name typed; an empty one, or the same, changes nothing.</summary>
    [RelayCommand]
    private void CommitRename(ModelNodeViewModel? node)
    {
        if (node is not { IsRenaming: true, Entity: { } entity })
        {
            return;
        }
        node.IsRenaming = false;
        string name = node.EditedName.Trim();
        if (name.Length > 0 && name != node.Name)
        {
            Edit?.Invoke(new RenameCommand(entity, name, _document));
        }
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "A command of the view model")]
    private void CancelRename(ModelNodeViewModel? node)
    {
        if (node is not null)
        {
            node.IsRenaming = false;
        }
    }

    private Document? _document;

    /// <summary>Shows the structure of a document, and follows its changes.</summary>
    public void Show(Document? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }
        _document = document;
        if (document is not null)
        {
            document.Changed += OnDocumentChanged;
        }

        SelectedNode = null;
        _context = null;
        Model = document is null ? null : ModelNodeViewModel.ForDocument(document);
    }

    partial void OnModelChanged(ModelNodeViewModel? oldValue, ModelNodeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnModelNodeChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnModelNodeChanged;
        }
    }

    private void OnModelNodeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelNodeViewModel.Children))
        {
            OnPropertyChanged(nameof(Roots));
        }
    }

    // A selected node that the change takes out of the tree is no longer selected.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        if (Model is not { } model)
        {
            return;
        }

        model.Apply(change);
        if (SelectedNode is { } selected && !model.Contains(selected))
        {
            SelectedNode = null;
        }
    }

    /// <summary>
    /// Selects the node of a path, the entities from the model to the node, opening the nodes above it. Null, or a
    /// path that is not in the document, selects none.
    /// </summary>
    [RelayCommand]
    private void Select(IReadOnlyList<Entity>? path)
    {
        SelectedNode = path is null ? null : Find(path);
    }

    /// <summary>
    /// Marks the node of the reference whose block is being edited, given the references entered from the model, and
    /// opens the nodes down to it; none in the model.
    /// </summary>
    public void ShowContext(IReadOnlyList<Entity> path)
    {
        ModelNodeViewModel? node = path.Count == 0 ? null : Find(path);
        if (_context is not null && !ReferenceEquals(_context, node))
        {
            _context.IsContext = false;
        }
        _context = node;
        if (node is not null)
        {
            node.IsContext = true;
            node.IsExpanded = true;
        }
    }

    private ModelNodeViewModel? _context;

    // The node of a path, opening the nodes above it; null for a path not in the document.
    private ModelNodeViewModel? Find(IReadOnlyList<Entity> path)
    {
        ModelNodeViewModel? node = Model;
        foreach (Entity entity in path)
        {
            if (node is null)
            {
                break;
            }

            node.IsExpanded = true;
            node = node.FindChild(entity, build: true);
        }
        return node;
    }
}
