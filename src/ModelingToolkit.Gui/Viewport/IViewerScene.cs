using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The scene of a viewer, as <see cref="ViewportScene"/> fills and updates it: implemented by <see cref="OcctViewer"/>,
/// and by a recording fake in the tests, which have no native views. Indices are given from 0 and, once removed, not
/// given again until <see cref="Clear"/>.
/// </summary>
internal interface IViewerScene
{
    /// <exception cref="ArgumentException">The viewer refuses the mesh: positions that are not finite, for example.</exception>
    /// <param name="shape">The tessellation the mesh is part of, whose elements are picked; null for a plain mesh.</param>
    /// <param name="faceMaterials">
    /// With a shape, the material of each of its faces, as returned by <see cref="AddMaterial"/>, or -1 for the one of
    /// the instance; null for that one everywhere.
    /// </param>
    int AddMesh(MeshEntity mesh, Polylines? edges, ShapeTessellation? shape, int[]? faceMaterials);

    void RemoveMesh(int mesh);

    int AddTexture(TextureImage image);

    int AddMaterial(Material material, int texture);

    /// <exception cref="ArgumentException">The placement is not invertible.</exception>
    int AddInstance(int mesh, int material, Transform placement);

    /// <exception cref="ArgumentException">The placement is not invertible.</exception>
    void SetInstance(int instance, int mesh, int material, Transform placement);

    void RemoveInstance(int instance);

    void SetSelection(ReadOnlySpan<int> instances);

    /// <summary>Highlights elements of the shapes of instances as selected, in place of those highlighted before.</summary>
    /// <exception cref="ArgumentException">An element is not one that an instance shows.</exception>
    void SetHighlightedElements(ReadOnlySpan<ViewerElement> elements);

    void Clear();

    void FitAll();
}
