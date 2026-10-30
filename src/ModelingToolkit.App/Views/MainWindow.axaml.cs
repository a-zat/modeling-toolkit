using Avalonia.Controls;
using ModelingToolkit.App.ViewModels;

namespace ModelingToolkit.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FileDialogs = new StorageProviderFileDialogs(this);
        }
    }
}
