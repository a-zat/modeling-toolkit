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

    /// <summary>
    /// The nodes selected in the outline, with Command or Shift: <see cref="SelectedNode"/> is the one that the rest of
    /// the application shows; the commands of the outline on one of them work on all.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<object> SelectedNodes { get; } = [];

    public ModelTreeViewModel() =>
        SelectedNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(GroupLabel));

    // The nodes a command on a node works on: all those selected, when it is one of several, without those inside
    // another one; else the node alone.
    private List<ModelNodeViewModel> TargetsOf(ModelNodeViewModel node)
    {
        List<ModelNodeViewModel> selected = [.. SelectedNodes.OfType<ModelNodeViewModel>().Where(n => n.Entity is not null)];
        if (selected.Count < 2 || !selected.Contains(node))
        {
            return [node];
        }
        var entities = new HashSet<Entity>(selected.Select(n => n.Entity!), ReferenceEqualityComparer.Instance);
        return [.. selected.Where(n => !n.Path.Take(n.Path.Count - 1).Any(entities.Contains))];
    }

    // The commands of the nodes, each built in the document as it is, applied from the last entity of each block to the
    // first, so that undoing puts each back where it was.
    private static IEnumerable<ModelNodeViewModel> InBlockOrder(IEnumerable<ModelNodeViewModel> nodes) =>
        nodes.OrderByDescending(n => n.Entity!.Owner?.Entities.IndexOf(n.Entity!) ?? -1);

    /// <summary>Deletes the entity of a node, or of all the nodes selected with it, as one command.</summary>
    [RelayCommand]
    private void Delete(ModelNodeViewModel? node)
    {
        if (node?.Entity is null || Edit is not { } edit)
        {
            return;
        }
        List<IEditCommand> commands = [.. InBlockOrder(TargetsOf(node).Where(n => n.Entity!.Owner is not null))
            .Select(n => new DeleteCommand(n.Entity!))];
        if (commands.Count > 0)
        {
            edit(commands.Count == 1 ? commands[0] : new CompositeCommand("Delete", commands));
        }
    }

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
            Edit?.Invoke(new SetHiddenCommand(TargetsOf(node).Select(n => n.Entity!), !entity.IsHidden));
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

    /// <summary>
    /// Moves the reference of a node into the block of another, where it stays in the project; with the other references
    /// selected with it, those that can go there.
    /// </summary>
    public bool MoveInto(ModelNodeViewModel dragged, ModelNodeViewModel target)
    {
        if (!CanMoveInto(dragged, target))
        {
            return false;
        }
        // A geometry has the path of the node it is the geometry of.
        IReadOnlyList<Entity> into = target.Path;
        List<ModelNodeViewModel> moved = [.. TargetsOf(dragged).Where(node => CanMoveInto(node, target))];
        int end = target.Block!.Entities.Count;
        List<IEditCommand> commands = [.. InBlockOrder(moved).Select(node => new MoveToBlockCommand(
            (BlockReference)node.Entity!, Placement(node.Path.Take(node.Path.Count - 1)), target.Block!, Placement(into), end))];
        if (Edit?.Invoke(commands.Count == 1 ? commands[0] : new CompositeCommand("Move into block", commands)) != true)
        {
            return false;
        }
        Select([.. into, dragged.Entity!]);
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
        // Each one up a level, into the block holding the one it is in.
        var commands = new List<IEditCommand>();
        foreach (ModelNodeViewModel moved in InBlockOrder(TargetsOf(node!).Where(CanMoveOut)))
        {
            IReadOnlyList<Entity> up = [.. moved.Path.Take(moved.Path.Count - 2)];
            Block target = up.Count == 0 ? root : ((BlockReference)up[^1]).Block;
            commands.Add(new MoveToBlockCommand((BlockReference)moved.Entity!,
                Placement(moved.Path.Take(moved.Path.Count - 1)), target, Placement(up), target.Entities.Count));
        }
        IReadOnlyList<Entity> into = [.. node!.Path.Take(node.Path.Count - 2)];
        if (Edit?.Invoke(commands.Count == 1 ? commands[0] : new CompositeCommand("Move out", commands)) == true)
        {
            Select([.. into, node.Entity!]);
        }
    }

    /// <summary>
    /// Puts the references of a node, or of the nodes selected with it in the same block, into a new group there, where
    /// they stay in the project; the group is selected.
    /// </summary>
    [RelayCommand]
    private void Group(ModelNodeViewModel? node)
    {
        if (node is not { Entity: BlockReference { Owner: { } owner } } || Model?.Document is not { } document)
        {
            return;
        }
        List<ModelNodeViewModel> grouped = [.. TargetsOf(node)
            .Where(n => n.Entity is BlockReference && ReferenceEquals(n.Entity.Owner, owner))];
        IReadOnlyList<Entity> into = [.. node.Path.Take(node.Path.Count - 1)];
        var made = new NewBlockCommand(document, owner, "Group");
        made.Reference.IsGroup = true;
        Core.Geometry.Transform placement = Placement(into);
        List<IEditCommand> commands = [made, .. InBlockOrder(grouped).Select(n => (IEditCommand)new MoveToBlockCommand(
            (BlockReference)n.Entity!, placement, made.Reference.Block, placement, 0))];
        if (Edit?.Invoke(new CompositeCommand("Group", commands)) == true)
        {
            Select([.. into, made.Reference]);
        }
    }

    /// <summary>
    /// The one command of the menu about groups: with several nodes selected, "Group" puts them in a new group; with
    /// one, it makes it a group, or a component again.
    /// </summary>
    public string GroupLabel
    {
        get
        {
            ModelNodeViewModel? first = SelectedNode ?? SelectedNodes.OfType<ModelNodeViewModel>().FirstOrDefault();
            List<ModelNodeViewModel> targets = first is null ? [] : TargetsOf(first);
            return targets.Any(n => n.IsLoose) ? "Make component"
                : targets.Count > 1 ? "Group"
                : first?.IsGroup == true ? "Convert to component" : "Make group";
        }
    }

    [RelayCommand]
    private void GroupOrToggle(ModelNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }
        List<ModelNodeViewModel> targets = TargetsOf(node);
        if (targets.Any(n => n.IsLoose))
        {
            MakeComponent(node);
        }
        else if (targets.Count > 1)
        {
            Group(node);
        }
        else
        {
            ToggleGroup(node);
        }
        OnPropertyChanged(nameof(GroupLabel));
    }

    /// <summary>
    /// Puts the meshes and shapes of a node, and of the nodes selected with it in the same block, with any references
    /// among them, into a new component placed there at the origin, where they stay in the project; it is selected.
    /// </summary>
    private void MakeComponent(ModelNodeViewModel node)
    {
        if (node.Entity is not { Owner: { } owner } || Model?.Document is not { } document)
        {
            return;
        }
        List<ModelNodeViewModel> made = [.. TargetsOf(node)
            .Where(n => n.Entity is { } entity && ReferenceEquals(entity.Owner, owner) &&
                        (n.IsLoose || n.Entity is BlockReference))];
        if (made.Count == 0)
        {
            return;
        }
        IReadOnlyList<Entity> into = [.. node.Path.Take(node.Path.Count - 1)];
        var component = new NewBlockCommand(document, owner, "Component");
        // Taken out from the last to the first, and put in from the first to the last, so that the order is kept.
        List<ModelNodeViewModel> inOrder = [.. made.OrderBy(n => owner.Entities.IndexOf(n.Entity!))];
        List<IEditCommand> commands =
        [
            component,
            .. InBlockOrder(made).Select(n => (IEditCommand)new DeleteCommand(n.Entity!)),
            .. inOrder.Select(n => new AddEntityCommand("Make component", component.Reference.Block, n.Entity!)),
        ];
        if (Edit?.Invoke(new CompositeCommand("Make component", commands)) == true)
        {
            Select([.. into, component.Reference]);
        }
    }

    partial void OnSelectedNodeChanged(ModelNodeViewModel? value) => OnPropertyChanged(nameof(GroupLabel));

    /// <summary>Makes the reference of a node, and those selected with it, groups, or components if it is a group.</summary>
    [RelayCommand]
    private void ToggleGroup(ModelNodeViewModel? node)
    {
        if (node?.Entity is not BlockReference reference)
        {
            return;
        }
        List<IEditCommand> commands = [.. TargetsOf(node).Select(n => n.Entity).OfType<BlockReference>()
            .Select(target => (IEditCommand)new SetGroupCommand(target, !reference.IsGroup))];
        Edit?.Invoke(commands.Count == 1 ? commands[0] : new CompositeCommand(reference.IsGroup ? "Convert to component" : "Make group", commands));
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
        if (node is { HasEntity: true } or { IsModel: true } && Edit is not null)
        {
            node.EditedName = node.Name;
            node.IsRenaming = true;
        }
    }

    /// <summary>Gives the entity the name typed; an empty one, or the same, changes nothing.</summary>
    [RelayCommand]
    private void CommitRename(ModelNodeViewModel? node)
    {
        if (node is not { IsRenaming: true })
        {
            return;
        }
        node.IsRenaming = false;
        string name = node.EditedName.Trim();
        if (name.Length == 0 || name == node.Name)
        {
            return;
        }
        if (node.IsModel && node.Document is { } model)
        {
            Edit?.Invoke(new RenameBlockCommand(model.Root, name)); // the name of the model
        }
        else if (node.Entity is { } entity)
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
