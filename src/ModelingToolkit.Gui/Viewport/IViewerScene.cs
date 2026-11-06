using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The scene of a viewer, as <see cref="ViewportScene"/> fills and updates it: implemented by <see cref="OcctViewer"/>,
/// and by a recording fake in the tests, which have no native views. Indices are given from 0 and, once removed, not
/// given again until <see cref="Clear"/>.
/// </summary>
internal interface IViewerScene
{
    /// <exception cref="ArgumentException">The viewer refuses the mesh: positions that are not finite, for example.</exception>
    int AddMesh(MeshEntity mesh, Polylines? edges);

    void RemoveMesh(int mesh);

    int AddTexture(TextureImage image);

    int AddMaterial(Material material, int texture);

    /// <exception cref="ArgumentException">The placement is not invertible.</exception>
    int AddInstance(int mesh, int material, Transform placement);

    /// <exception cref="ArgumentException">The placement is not invertible.</exception>
    void SetInstance(int instance, int mesh, int material, Transform placement);

    void RemoveInstance(int instance);

    void SetSelection(ReadOnlySpan<int> instances);

    void Clear();

    void FitAll();
}
