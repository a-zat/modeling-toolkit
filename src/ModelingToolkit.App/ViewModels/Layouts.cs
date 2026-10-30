namespace ModelingToolkit.App.ViewModels;

/// <summary>The layouts of the prototype (#10): one of them will be kept.</summary>
public enum LayoutKind
{
    /// <summary>A: toolbar, model tree on the left, properties on the right.</summary>
    Classic,

    /// <summary>B: activity bar with one panel at a time, properties of the selection, command palette.</summary>
    Compact,
}

/// <summary>The side panels of layout B, shown one at a time.</summary>
public enum SidePanel
{
    None,
    Model,
    Materials,
}
