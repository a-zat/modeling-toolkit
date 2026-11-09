using System.Numerics;
using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// What a click of the 3D view picks, with the modifier keys held.
/// </summary>
/// <param name="Path">The path of the mesh or shape under the pointer, a leaf of the model tree; null for nothing.</param>
/// <param name="Element">The face, edge or vertex of a shape under the pointer; null on a mesh or on nothing.</param>
/// <param name="Point">The point picked, in the model space.</param>
/// <param name="Normal">The normal of the face at the point, of unit length; zero elsewhere.</param>
public sealed record ViewportPick(
    IReadOnlyList<Entity>? Path, ElementReference? Element, Point3d Point, Vector3 Normal, KeyModifiers Modifiers);
