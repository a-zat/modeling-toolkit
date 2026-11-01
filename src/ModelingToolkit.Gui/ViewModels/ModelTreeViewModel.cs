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

    public void Show(Document? document)
    {
        SelectedNode = null;
        Roots = document is null ? [] : [ModelNodeViewModel.ForDocument(document)];
    }

    /// <summary>
    /// Selects the node of a path, the entities from the model to the node, opening the nodes above it. Null, or a
    /// path that is not in the document, selects none.
    /// </summary>
    [RelayCommand]
    private void Select(IReadOnlyList<Entity>? path)
    {
        ModelNodeViewModel? node = path is null ? null : Roots.FirstOrDefault();
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
