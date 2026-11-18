using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class SaveChangesWindow : Window
{
    public SaveChangesWindow() : this("this project")
    {
    }

    public SaveChangesWindow(string name)
    {
        InitializeComponent();
        Question.Text = $"Do you want to save the changes made to {name}?";
        Save.Click += (_, _) => Close(SaveChoice.Save);
        DontSave.Click += (_, _) => Close(SaveChoice.DontSave);
        Cancel.Click += (_, _) => Close(SaveChoice.Cancel);
    }
}
