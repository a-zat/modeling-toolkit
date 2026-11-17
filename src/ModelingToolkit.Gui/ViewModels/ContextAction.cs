using System.Windows.Input;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>An entry of the menu of the 3D view: a command, with its key; null command for a separator.</summary>
public sealed record ContextAction(string Header, ICommand? Command, object? Parameter = null, string? Gesture = null)
{
    public static ContextAction Separator { get; } = new("-", null);

    public bool IsSeparator => Command is null;
}
