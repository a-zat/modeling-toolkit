using CommunityToolkit.Mvvm.ComponentModel;
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
}
