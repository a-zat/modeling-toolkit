using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class LayersView : UserControl
{
    public LayersView()
    {
        InitializeComponent();
        Tree.AddHandler(DoubleTappedEvent, OnTreeDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnTreeLostFocus, RoutingStrategies.Bubble);
    }

    private LayersViewModel? Model => DataContext as LayersViewModel;

    // The entities on no layer are not a layer: they have no menu.
    private void OnRowContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: LayerRowViewModel { IsLayer: false } })
        {
            e.Handled = true;
        }
    }

    // A double click on a layer edits its name.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not TextBox &&
            (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { DataContext: LayerRowViewModel row })
        {
            Model?.StartRenameCommand.Execute(row);
            e.Handled = true;
        }
    }

    // In the name being edited, Enter keeps it and Escape leaves it as it was.
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model || e.Source is not TextBox { DataContext: LayerRowViewModel { IsRenaming: true } row })
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            model.CommitRenameCommand.Execute(row);
            Tree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.IsRenaming = false; // as it was
            Tree.Focus();
            e.Handled = true;
        }
    }

    // Clicking elsewhere keeps the name typed.
    private void OnTreeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: LayerRowViewModel { IsRenaming: true } row })
        {
            Model?.CommitRenameCommand.Execute(row);
        }
    }

    private void OnRenameBoxAttached(object? sender, VisualTreeAttachmentEventArgs e) => FocusWhenShown(sender as TextBox);

    private void OnRenameBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            FocusWhenShown(sender as TextBox);
        }
    }

    private static void FocusWhenShown(TextBox? box)
    {
        if (box is not { IsVisible: true })
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (box.IsEffectivelyVisible)
            {
                box.Focus();
                box.SelectAll();
            }
        }, DispatcherPriority.Loaded);
    }
}
