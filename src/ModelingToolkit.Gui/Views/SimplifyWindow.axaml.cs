using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class SimplifyWindow : Window
{
    public SimplifyWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SimplifyViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
            // Framed once the window has its size and the result is there.
            viewModel.FirstShown += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => Viewport.FitAll(), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (DataContext as SimplifyViewModel)?.Dispose();
    }
}
