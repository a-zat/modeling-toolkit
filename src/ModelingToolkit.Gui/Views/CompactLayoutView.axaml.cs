using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

public partial class CompactLayoutView : UserControl
{
    private readonly CollapsibleColumn _inspectorColumn;
    private readonly CollapsibleRow _outlineRow;
    private MainWindowViewModel? _viewModel;

    public CompactLayoutView()
    {
        InitializeComponent();
        _inspectorColumn = new CollapsibleColumn(Panels.ColumnDefinitions[3]);
        // Set here rather than bound, which the popup might not have yet when it opens.
        TimeOfDayPopup.PlacementTarget = Viewport;
        Viewport.MenuRequested += OnViewportContextRequested;
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                PlaceTimeOfDay();
            }
        };
        _outlineRow = new CollapsibleRow(Inspector.RowDefinitions[1]);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Camera = Viewport;
            _viewModel.PropertyChanged += OnPanelsChanged;
            UpdatePanels(_viewModel);
        }
        UpdateTimeOfDay();
    }

    // A popup stays where it was opened when the 3D view changes size, as when the inspector closes: it is opened again.
    private void PlaceTimeOfDay()
    {
        if (TimeOfDayPopup.IsOpen)
        {
            TimeOfDayPopup.Close();
            TimeOfDayPopup.Open();
        }
    }

    private void UpdateTimeOfDay() => TimeOfDayPopup.IsOpen = _viewModel is { IsRendered: true };

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.Camera = null;
            _viewModel.PropertyChanged -= OnPanelsChanged;
            _viewModel = null;
        }
        TimeOfDayPopup.IsOpen = false;
    }

    private void OnPanelsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsInspectorOpen) or nameof(MainWindowViewModel.IsOutlineExpanded))
        {
            UpdatePanels(_viewModel!);
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.IsRendered))
        {
            UpdateTimeOfDay();
        }
    }

    // The grid keeps the space of hidden columns and rows: they are given none, and their size back when shown.
    private void UpdatePanels(MainWindowViewModel viewModel)
    {
        _inspectorColumn.Show(viewModel.IsInspectorOpen);
        _outlineRow.Show(viewModel.IsOutlineExpanded);
    }

    // A menu of what can be done with what was right-clicked, at the pointer.
    private void OnViewportContextRequested(object? sender, Viewport.ViewportPick pick)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.ContextActions(pick) is not { Count: > 0 } actions)
        {
            return;
        }
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (ContextAction action in actions)
        {
            menu.Items.Add(action.IsSeparator
                ? new Separator()
                : new MenuItem
                {
                    Header = action.Header,
                    Command = action.Command,
                    CommandParameter = action.Parameter,
                    InputGesture = action.Gesture is { } gesture ? KeyGesture.Parse(Gesture(gesture)) : null,
                });
        }
        menu.Open(Viewport);
    }

    // The keys as Avalonia names them, from the symbols the menu shows.
    private static string Gesture(string symbols) => symbols
        .Replace("⇧", "Shift+", StringComparison.Ordinal)
        .Replace("⌘", "Meta+", StringComparison.Ordinal)
        .Replace("⌫", "Back", StringComparison.Ordinal);
}
