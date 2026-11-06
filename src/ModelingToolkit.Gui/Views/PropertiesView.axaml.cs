using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace ModelingToolkit.Gui.Views;

public partial class PropertiesView : UserControl
{
    public PropertiesView() => InitializeComponent();

    // Enter renames the entity at once, Escape goes back to its name; the field keeps the focus.
    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BindingOperations.GetBindingExpressionBase(NameBox, TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            BindingOperations.GetBindingExpressionBase(NameBox, TextBox.TextProperty)?.UpdateTarget();
            e.Handled = true;
        }
    }
}
