using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Editing.Tools;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A button of the tool column.</summary>
/// <param name="icon">The key of the icon resource, such as "Icon.Select".</param>
public sealed partial class ToolItemViewModel(Tool tool, string icon) : ObservableObject
{
    public Tool Tool { get; } = tool;

    public string Icon { get; } = icon;

    public string Name => Tool.Name;

    /// <summary>Whether the tool is the one the 3D view uses.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
