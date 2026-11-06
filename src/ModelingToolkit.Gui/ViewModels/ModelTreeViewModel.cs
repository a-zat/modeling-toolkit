using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>The structure of the document, as the model tree shows it, and the selected node.</summary>
public sealed partial class ModelTreeViewModel : ObservableObject
{
    public ModelTreeViewModel() => Roots = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<ModelNodeViewModel> Roots { get; private set; }

    [ObservableProperty]
    public partial ModelNodeViewModel? SelectedNode { get; set; }

    public bool IsEmpty => Roots.Count == 0;

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
        Roots = document is null ? [] : [ModelNodeViewModel.ForDocument(document)];
    }

    // A selected node that the change takes out of the tree is no longer selected.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        if (Roots.Count == 0)
        {
            return;
        }

        Roots[0].Apply(change);
        if (SelectedNode is { } selected && !Roots[0].Contains(selected))
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
        ModelNodeViewModel? node = path is null || Roots.Count == 0 ? null : Roots[0];
        foreach (Entity entity in path ?? [])
        {
            if (node is null)
            {
                break;
            }

            node.IsExpanded = true;
            node = node.Children.FirstOrDefault(child => ReferenceEquals(child.Entity, entity));
        }

        SelectedNode = node;
    }
}
