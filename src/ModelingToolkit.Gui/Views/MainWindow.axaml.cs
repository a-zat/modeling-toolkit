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
        AddHandler(KeyDownEvent, OnKeyPressed, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, e) => Typing(e.Source is TextBox), RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, (_, _) => Typing(false), RoutingStrategies.Bubble);
    }

    private void Typing(bool typing)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsTypingInField = typing;
        }
    }

    // Tab, outside the fields, passes between the move and rotate tools, keeping the point taken.
    private void OnKeyPressed(object? sender, KeyEventArgs e)
    {
        // In a field, they delete text, never the selection.
        if (e.Key is Key.Delete or Key.Back && e.Source is not TextBox &&
            e.KeyModifiers is KeyModifiers.None or KeyModifiers.Meta or KeyModifiers.Control &&
            DataContext is MainWindowViewModel deleting && deleting.DeleteCommand.CanExecute(null))
        {
            deleting.DeleteCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox &&
            DataContext is MainWindowViewModel viewModel && viewModel.SwitchMoveRotate())
        {
            e.Handled = true;
        }
    }

    private void OnTextTyped(object? sender, TextInputEventArgs e)
    {
        // X, Y and Z typed outside the fields lock the axis, as the arrows do.
        if (e.Source is not TextBox && e.Text is [var letter] && char.ToUpperInvariant(letter) is 'X' or 'Y' or 'Z' &&
            DataContext is MainWindowViewModel axes)
        {
            axes.PressToolKeyCommand.Execute(char.ToUpperInvariant(letter) switch
            {
                'X' => Core.Editing.Tools.ToolKey.Right,
                'Y' => Core.Editing.Tools.ToolKey.Left,
                _ => Core.Editing.Tools.ToolKey.Up,
            });
            e.Handled = true;
            return;
        }
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
