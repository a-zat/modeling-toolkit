using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class CompactLayoutView : UserControl
{
    private readonly CollapsibleColumn _sideColumn;
    private readonly CollapsibleColumn _propertiesColumn;
    private MainWindowViewModel? _viewModel;

    public CompactLayoutView()
    {
        InitializeComponent();
        _sideColumn = new CollapsibleColumn(Panels.ColumnDefinitions[1]);
        _propertiesColumn = new CollapsibleColumn(Panels.ColumnDefinitions[5]);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Camera = Viewport;
            _viewModel.PropertyChanged += OnPanelsChanged;
            _viewModel.Properties.PropertyChanged += OnPanelsChanged;
            UpdateColumns(_viewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.Camera = null;
            _viewModel.PropertyChanged -= OnPanelsChanged;
            _viewModel.Properties.PropertyChanged -= OnPanelsChanged;
            _viewModel = null;
        }
    }

    private void OnPanelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsSidePanelOpen) or nameof(PropertiesViewModel.HasSelection))
        {
            UpdateColumns(_viewModel!);
        }
    }

    private void UpdateColumns(MainWindowViewModel viewModel)
    {
        _sideColumn.Show(viewModel.IsSidePanelOpen);
        _propertiesColumn.Show(viewModel.Properties.HasSelection);
    }
}
