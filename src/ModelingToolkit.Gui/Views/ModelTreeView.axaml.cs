using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnTreeLostFocus, RoutingStrategies.Bubble);
    }

    private ModelTreeViewModel? Model => DataContext as ModelTreeViewModel;

    // A double click on a node edits its name.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is TextBox)
        {
            return;
        }
        if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { DataContext: ModelNodeViewModel node } item)
        {
            StartRename(node, item);
            e.Handled = true;
        }
    }

    // F2 renames the selected node; in the name being edited, Enter keeps it and Escape leaves it as it was.
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }
        if (e.Source is TextBox { DataContext: ModelNodeViewModel { IsRenaming: true } renamed })
        {
            if (e.Key == Key.Enter)
            {
                model.CommitRenameCommand.Execute(renamed);
                Tree.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                model.CancelRenameCommand.Execute(renamed);
                Tree.Focus();
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.F2 && model.SelectedNode is { } selected &&
            Tree.TreeContainerFromItem(selected) is TreeViewItem item)
        {
            StartRename(selected, item);
            e.Handled = true;
        }
    }

    // Clicking elsewhere keeps the name typed.
    private void OnTreeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: ModelNodeViewModel { IsRenaming: true } renamed })
        {
            Model?.CommitRenameCommand.Execute(renamed);
        }
    }

    private void StartRename(ModelNodeViewModel node, TreeViewItem item)
    {
        if (Model is not { } model || !model.StartRenameCommand.CanExecute(node))
        {
            return;
        }
        model.StartRenameCommand.Execute(node);
        // Once the box is shown.
        Dispatcher.UIThread.Post(() =>
        {
            if (item.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.IsVisible) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }
}
