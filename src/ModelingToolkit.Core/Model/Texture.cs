namespace ModelingToolkit.Core.Model;

/// <summary>An image used by materials, kept encoded as in its source (e.g. PNG or JPEG bytes).</summary>
public sealed class Texture
{
    public Texture(string name, string mediaType, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentNullException.ThrowIfNull(data);

        Name = name;
        MediaType = mediaType;
        Data = data;
    }

    public string Name { get; set; }

    /// <summary>Media type of <see cref="Data"/>, e.g. <c>image/png</c>.</summary>
    public string MediaType { get; }

    public byte[] Data { get; }
}
