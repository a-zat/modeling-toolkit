using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class ClassicLayoutView : UserControl
{
    private readonly CollapsibleColumn _modelColumn;
    private readonly CollapsibleColumn _propertiesColumn;
    private MainWindowViewModel? _viewModel;

    public ClassicLayoutView()
    {
        InitializeComponent();
        _modelColumn = new CollapsibleColumn(Panels.ColumnDefinitions[0]);
        _propertiesColumn = new CollapsibleColumn(Panels.ColumnDefinitions[4]);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateColumns(_viewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsModelPanelVisible)
            or nameof(MainWindowViewModel.IsPropertiesPanelVisible))
        {
            UpdateColumns(_viewModel!);
        }
    }

    private void UpdateColumns(MainWindowViewModel viewModel)
    {
        _modelColumn.Show(viewModel.IsModelPanelVisible);
        _propertiesColumn.Show(viewModel.IsPropertiesPanelVisible);
    }
}
