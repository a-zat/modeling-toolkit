using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [NotifyPropertyChangedFor(nameof(Roots), nameof(IsEmpty))]
    public partial ModelNodeViewModel? Model { get; private set; }

    /// <summary>The nodes at the top of the outline: the references and the geometry of the model.</summary>
    public IReadOnlyList<ModelNodeViewModel> Roots => Model?.Children ?? [];

    [ObservableProperty]
    public partial ModelNodeViewModel? SelectedNode { get; set; }

    public bool IsEmpty => Model is null;

    /// <summary>Executed with a node that is double-clicked: one of a reference enters its block.</summary>
    public System.Windows.Input.ICommand? EnterCommand { get; set; }

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
