using Avalonia.Controls;
using Avalonia.Threading;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        ResultList.Tapped += (_, _) => (DataContext as CommandPaletteViewModel)?.RunCommand.Execute(null);
    }

    /// <summary>
    /// Gives the keyboard to the search box, once the popup of the palette is shown. On macOS the popup is a window of
    /// its own, which does not take the keyboard by itself: it is activated first.
    /// </summary>
    public void TakeKeyboard() => Dispatcher.UIThread.Post(() =>
    {
        (TopLevel.GetTopLevel(this) as WindowBase)?.Activate();
        SearchBox.Focus();
    }, DispatcherPriority.Background);
}
