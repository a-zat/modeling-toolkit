using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Tests;

/// <summary>
/// A viewer without a view, for the tests: it keeps what it shows, as the native one, and records the changes of its
/// instances and meshes.
/// </summary>
internal sealed class RecordingViewer : IViewerScene
{
    private int _nextMesh;
    private int _nextInstance;

    /// <summary>The meshes added and not removed, by index.</summary>
    public Dictionary<int, MeshEntity> Meshes { get; } = [];

    /// <summary>The instances added and not removed, by index.</summary>
    public Dictionary<int, (int Mesh, int Material, Transform Placement)> Instances { get; } = [];

    public List<Material> Materials { get; } = [];

    public int[] Selection { get; private set; } = [];

    /// <summary>The changes of the meshes and the instances since <see cref="Forget"/>: "add instance 3", for example.</summary>
    public List<string> Changes { get; } = [];

    public void Forget() => Changes.Clear();

    public int AddMesh(MeshEntity mesh, Polylines? edges, ShapeTessellation? shape)
    {
        Meshes[_nextMesh] = mesh;
        Changes.Add($"add mesh {_nextMesh}");
        return _nextMesh++;
    }

    public void RemoveMesh(int mesh)
    {
        Assert.DoesNotContain(Instances.Values, instance => instance.Mesh == mesh);
        Assert.True(Meshes.Remove(mesh));
        Changes.Add($"remove mesh {mesh}");
    }

    public int AddTexture(TextureImage image) => 0;

    public int AddMaterial(Material material, int texture)
    {
        Materials.Add(material);
        return Materials.Count - 1;
    }

    public int AddInstance(int mesh, int material, Transform placement)
    {
        Assert.True(Meshes.ContainsKey(mesh));
        Instances[_nextInstance] = (mesh, material, placement);
        Changes.Add($"add instance {_nextInstance}");
        return _nextInstance++;
    }

    public void SetInstance(int instance, int mesh, int material, Transform placement)
    {
        Assert.True(Instances.ContainsKey(instance));
        Assert.True(Meshes.ContainsKey(mesh));
        Instances[instance] = (mesh, material, placement);
        Changes.Add($"set instance {instance}");
    }

    public void RemoveInstance(int instance)
    {
        Assert.True(Instances.Remove(instance));
        Changes.Add($"remove instance {instance}");
    }

    public void SetSelection(ReadOnlySpan<int> instances)
    {
        foreach (int instance in instances)
        {
            Assert.True(Instances.ContainsKey(instance));
        }
        Selection = instances.ToArray();
    }

    /// <summary>The elements highlighted as selected.</summary>
    public ViewerElement[] HighlightedElements { get; private set; } = [];

    public void SetHighlightedElements(ReadOnlySpan<ViewerElement> elements)
    {
        foreach (ViewerElement element in elements)
        {
            Assert.True(Instances.ContainsKey(element.Instance));
        }
        HighlightedElements = elements.ToArray();
    }

    public void Clear()
    {
        Meshes.Clear();
        Instances.Clear();
        Materials.Clear();
        _nextMesh = 0;
        _nextInstance = 0;
    }

    public void FitAll()
    {
    }
}
