using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class PropertiesView : UserControl
{
    public PropertiesView() => InitializeComponent();

    private PropertiesViewModel? ViewModel => DataContext as PropertiesViewModel;

    // A double click on the title edits it, the name selected.
    private void OnTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { IsEditable: true } viewModel)
        {
            return;
        }
        viewModel.StartRenameCommand.Execute(null);
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
        e.Handled = true;
    }

    // Enter renames the entity at once, Escape goes back to its name; either ends the editing of the title.
    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BindingOperations.GetBindingExpressionBase(NameBox, TextBox.TextProperty)?.UpdateSource();
            EndRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BindingOperations.GetBindingExpressionBase(NameBox, TextBox.TextProperty)?.UpdateTarget();
            EndRename();
            e.Handled = true;
        }
    }

    private void OnNameLostFocus(object? sender, RoutedEventArgs e) => EndRename();

    private void EndRename()
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.IsRenaming = false;
        }
    }

    // Enter applies a field at once, Escape goes back to its value; the field keeps the focus.
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateTarget();
            e.Handled = true;
        }
    }
}
