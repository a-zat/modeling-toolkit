using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class StatusBarView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public StatusBarView()
    {
        InitializeComponent();
        MeasurementField.KeyDown += OnMeasurementKeyDown;
        MeasurementField.LostFocus += (_, _) => ShowMeasurement();
        StatusText.PointerPressed += OnStatusPressed;
        StatusText.PointerExited += (_, _) =>
        {
            _copied?.Dispose();
            _copied = null;
        };
    }

    private async void OnStatusPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not { StatusMessage: { Length: > 0 } message } ||
            TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }
        e.Handled = true;
        string text = _viewModel.StatusDetails is { } details ? $"{message}{Environment.NewLine}{details}" : message;
        await clipboard.SetTextAsync(text);
        // Over the tip bound, until the pointer leaves.
        _copied?.Dispose();
        _copied = StatusText.SetValue(ToolTip.TipProperty, "Copied", BindingPriority.Animation);
        ToolTip.SetIsOpen(StatusText, true);
    }

    private IDisposable? _copied;

    /// <summary>
    /// Starts typing a value in the measurement field with text typed elsewhere, such as in the 3D view; false when the
    /// active tool takes no value now.
    /// </summary>
    public bool TakeTyping(string text)
    {
        if (_viewModel is not { HasMeasurement: true } || MeasurementField.IsFocused)
        {
            return false;
        }
        MeasurementField.Text = text;
        MeasurementField.Focus();
        MeasurementField.CaretIndex = text.Length;
        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
        ShowMeasurement();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Measurement))
        {
            ShowMeasurement();
        }
    }

    // The measure of the tool, unless a value is being typed in its place.
    private void ShowMeasurement()
    {
        if (!MeasurementField.IsFocused)
        {
            MeasurementField.Text = _viewModel?.Measurement;
        }
    }

    // Enter gives the value to the tool, Escape drops it; either way the keys go back to the 3D view.
    private void OnMeasurementKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }
        string text = MeasurementField.Text ?? string.Empty;
        // Back to the 3D view, so that the next value typed starts afresh.
        TopLevel.GetTopLevel(this)?.GetVisualDescendants().OfType<Viewport.ViewportView>().FirstOrDefault()?.Focus();
        if (e.Key == Key.Enter)
        {
            _viewModel?.TypeMeasurementCommand.Execute(text);
            e.Handled = true;
        }
        ShowMeasurement();
    }
}
