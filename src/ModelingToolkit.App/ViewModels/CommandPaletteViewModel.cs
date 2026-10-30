using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModelingToolkit.App.ViewModels;

/// <summary>A command offered by the command palette.</summary>
/// <param name="Gesture">Keyboard shortcut shown next to the name, if any.</param>
public sealed record PaletteCommand(string Name, ICommand Command, object? Parameter = null, string? Gesture = null);

/// <summary>Searchable list of commands, opened with Ctrl+K (⌘K on macOS).</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private IReadOnlyList<PaletteCommand> _commands = [];

    public CommandPaletteViewModel()
    {
        Query = string.Empty;
        Results = [];
    }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<PaletteCommand> Results { get; private set; }

    [ObservableProperty]
    public partial PaletteCommand? SelectedResult { get; set; }

    public IReadOnlyList<PaletteCommand> Commands
    {
        get => _commands;
        set
        {
            _commands = value;
            Filter();
        }
    }

    partial void OnQueryChanged(string value) => Filter();

    [RelayCommand]
    private void Open()
    {
        Query = string.Empty;
        Filter();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Run()
    {
        PaletteCommand? command = SelectedResult;
        IsOpen = false;
        if (command is not null && command.Command.CanExecute(command.Parameter))
        {
            command.Command.Execute(command.Parameter);
        }
    }

    [RelayCommand]
    private void SelectNext() => MoveSelection(1);

    [RelayCommand]
    private void SelectPrevious() => MoveSelection(-1);

    private void MoveSelection(int offset)
    {
        if (Results.Count == 0)
        {
            return;
        }

        int index = SelectedResult is null ? -1 : Results.ToList().IndexOf(SelectedResult);
        SelectedResult = Results[Math.Clamp(index + offset, 0, Results.Count - 1)];
    }

    private void Filter()
    {
        Results = [.. _commands.Where(command => command.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))];
        SelectedResult = Results.FirstOrDefault();
    }
}
