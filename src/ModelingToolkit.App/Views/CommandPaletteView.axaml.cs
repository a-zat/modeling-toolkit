using Avalonia;
using Avalonia.Controls;
using ModelingToolkit.App.ViewModels;

namespace ModelingToolkit.App.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        ResultList.Tapped += (_, _) => (DataContext as CommandPaletteViewModel)?.RunCommand.Execute(null);
    }

    // The palette lives in a popup, attached when it opens: the search box takes the focus right away.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SearchBox.Focus();
    }
}
