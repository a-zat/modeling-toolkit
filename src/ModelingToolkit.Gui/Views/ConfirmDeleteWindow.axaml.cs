using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class ConfirmDeleteWindow : Window
{
    public ConfirmDeleteWindow() : this("this item", 1)
    {
    }

    public ConfirmDeleteWindow(string what, int count)
    {
        InitializeComponent();
        Question.Text = $"Delete {what}?";
        Detail.Text = $"It holds {Formatting.Count(count, "item", "items")}, which are deleted with it. You can undo the deletion.";
        Confirm.Click += (_, _) => Close(true);
        Cancel.Click += (_, _) => Close(false);
    }
}
