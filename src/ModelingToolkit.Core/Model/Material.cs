namespace ModelingToolkit.Core.Model;

/// <summary>Physically based material, following the metallic-roughness model of glTF.</summary>
public sealed class Material
{
    public Material(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public string Name { get; set; }

    /// <summary>Base colour; its alpha is the opacity of the material.</summary>
    public Color BaseColor { get; set; } = Color.White;

    /// <summary>From 0 (dielectric, e.g. plastic or wood) to 1 (metal).</summary>
    public float Metallic { get; set; }

    /// <summary>From 0 (smooth, mirror-like) to 1 (rough, fully diffuse).</summary>
    public float Roughness { get; set; } = 1f;

    /// <summary>Image multiplied by <see cref="BaseColor"/>, mapped through the texture coordinates of the meshes.</summary>
    public Texture? BaseColorTexture { get; set; }
}
