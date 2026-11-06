using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

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

    // Closing while a file is opened or saved waits for it (see WhenIdleAsync), then closes.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && DataContext is MainWindowViewModel { IsBusy: true } viewModel)
        {
            e.Cancel = true;
            _ = CloseWhenIdleAsync(viewModel);
        }
    }

    private async Task CloseWhenIdleAsync(MainWindowViewModel viewModel)
    {
        await viewModel.WhenIdleAsync();
        Close();
    }

    private void OnPaletteOpened(object? sender, EventArgs e) => Palette.TakeKeyboard();
}
