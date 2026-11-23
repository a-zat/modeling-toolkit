using System.Numerics;
using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>The inputs of the pointer in the 3D view that tools receive.</summary>
public enum ViewportInputKind
{
    /// <summary>The pointer moved without buttons pressed.</summary>
    Hover,
    Press,
    /// <summary>The pointer moved with the left button pressed.</summary>
    Drag,
    Release,
    /// <summary>The left button was pressed and released without a drag, after the release.</summary>
    Click,
}

/// <summary>
/// An input of the pointer in the 3D view: what is under the pointer, with the modifier keys held.
/// </summary>
/// <param name="Path">The path of the mesh or shape under the pointer, a leaf of the model tree; null for nothing.</param>
/// <param name="Element">The face, edge or vertex of a shape under the pointer; null on a mesh or on nothing.</param>
/// <param name="Point">The point picked, in the model space.</param>
/// <param name="Normal">The normal of the face at the point, of unit length; zero elsewhere.</param>
/// <param name="ClickCount">1 for a click, 2 for the second click of a double click, and so on.</param>
/// <param name="Kind">The input.</param>
/// <param name="Ray">The ray of the pointer, in the model space; null when the view has none.</param>
/// <param name="Handle">
/// For a press, a drag and a release, the index of the handle pressed in <see cref="ViewportView.Handles"/>, or
/// <see cref="Core.Editing.Tools.SelectTool.SelectionBody"/> for the selection taken elsewhere; null for none.
/// </param>
public sealed record ViewportPick(
    IReadOnlyList<Entity>? Path, ElementReference? Element, Point3d Point, Vector3 Normal, KeyModifiers Modifiers,
    int ClickCount = 1, ViewportInputKind Kind = ViewportInputKind.Click, PointerRay? Ray = null, int? Handle = null);
