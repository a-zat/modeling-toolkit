using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Digits typed outside the fields go to the measurement field, as the value of the active tool.
        AddHandler(TextInputEvent, OnTextTyped, RoutingStrategies.Tunnel);
    }

    private void OnTextTyped(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text) || !(char.IsDigit(e.Text[0]) || e.Text[0] is '.' or ','))
        {
            return;
        }
        if (this.GetVisualDescendants().OfType<StatusBarView>().FirstOrDefault() is { } statusBar &&
            statusBar.TakeTyping(e.Text))
        {
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.FileDialogs = new StorageProviderFileDialogs(this);
            viewModel.ShowSimplify = simplify => new SimplifyWindow { DataContext = simplify }.ShowDialog(this);
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
