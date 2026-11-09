using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class ModelTreeView : UserControl
{
    public ModelTreeView() => InitializeComponent();

    // A double click on a node enters it, rather than opening or closing it.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext is
                ModelNodeViewModel node &&
            DataContext is ModelTreeViewModel { EnterCommand: { } command } && command.CanExecute(node))
        {
            command.Execute(node);
            e.Handled = true;
        }
    }
}
