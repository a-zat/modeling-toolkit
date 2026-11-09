using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class ModelTreeView : UserControl
{
    public ModelTreeView()
    {
        InitializeComponent();
        // The items handle the double clicks that open and close them: the handler takes those too.
        Tree.AddHandler(DoubleTappedEvent, OnTreeDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // A double click on the node of a reference enters its block.
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
