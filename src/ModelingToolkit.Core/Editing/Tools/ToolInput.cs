using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>What is under the pointer, as the 3D view picks it.</summary>
/// <param name="Path">The path of the mesh or shape under the pointer, from the model; null for nothing.</param>
/// <param name="Element">The face, edge or vertex of a shape under the pointer; null on a mesh or on nothing.</param>
/// <param name="Point">The point picked, in the model space.</param>
/// <param name="Normal">The normal of the face at the point, of unit length; zero elsewhere.</param>
/// <param name="Ray">The ray of the pointer, in the model space, also over nothing; null when the view has none.</param>
public sealed record ToolPick(
    IReadOnlyList<Entity>? Path, ElementReference? Element, Point3d Point, Vector3d Normal, PointerRay? Ray = null)
{
    public static ToolPick Nothing { get; } = new(null, null, default, default);
}

/// <summary>The modifier keys held.</summary>
[Flags]
public enum ToolModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    /// <summary>The Command key of macOS.</summary>
    Meta = 8,
}

/// <summary>An input of the pointer, with what it picks and the modifier keys held.</summary>
/// <param name="ClickCount">For a click: 1, or 2 for the second click of a double click, and so on.</param>
public sealed record ToolInput(ToolPick Pick, ToolModifiers Modifiers = ToolModifiers.None, int ClickCount = 1);

/// <summary>The keys that tools can take.</summary>
public enum ToolKey
{
    Escape,
    Enter,
}
