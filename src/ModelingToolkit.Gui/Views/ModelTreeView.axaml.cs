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
        // A reference dragged onto another node goes into its block.
        Tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerMovedEvent, OnTreePointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tree.AddHandler(PointerReleasedEvent, OnTreePointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private const double DragDistance = 6; // before a press becomes a drag, in points

    private ModelNodeViewModel? _pressed;
    private Point _pressedAt;
    private bool _dragging;
    private TreeViewItem? _dropTarget;

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed &&
            NodeAt(e.Source) is { Node: { IsReference: true, IsRenaming: false } node } ? node : null;
        _pressedAt = e.GetPosition(Tree);
        _dragging = false;
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } dragged || Model is not { } model)
        {
            return;
        }
        Point at = e.GetPosition(Tree);
        if (!_dragging && Math.Abs(at.X - _pressedAt.X) + Math.Abs(at.Y - _pressedAt.Y) < DragDistance)
        {
            return;
        }
        _dragging = true;
        TreeViewItem? target = NodeAt(Tree.InputHitTest(at)) is { } hit && model.CanMoveInto(dragged, hit.Node)
            ? hit.Item
            : null;
        SetDropTarget(target);
        e.Handled = true;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressed is { } dragged && _dropTarget?.DataContext is ModelNodeViewModel target)
        {
            Model?.MoveInto(dragged, target);
            e.Handled = true;
        }
        _pressed = null;
        _dragging = false;
        SetDropTarget(null);
    }

    private void SetDropTarget(TreeViewItem? item)
    {
        if (ReferenceEquals(item, _dropTarget))
        {
            return;
        }
        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = item;
        _dropTarget?.Classes.Add("dropTarget");
    }

    private static (TreeViewItem Item, ModelNodeViewModel Node)? NodeAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true) is
            { DataContext: ModelNodeViewModel node } item
            ? (item, node)
            : null;

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
        if (Model is { } model && model.StartRenameCommand.CanExecute(node))
        {
            model.StartRenameCommand.Execute(node);
        }
    }

    // The box of a name being edited takes the keys as it is shown, however the renaming started: from the menu, or
    // for a block just added, whose item is made with the box shown.
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
