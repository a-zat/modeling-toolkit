namespace ModelingToolkit.IO.Native;

/// <summary>
/// The native file format: a zip archive holding <c>manifest.json</c> (textures, materials, blocks and their
/// entities), one binary file per mesh and the textures as encoded images.
/// </summary>
/// <remarks>
/// A mesh file contains, little-endian and in this order: the vertices (3 doubles each), the triangles (3 int32
/// each), then, when the manifest says so, the normals (3 floats each) and the texture coordinates (2 floats each).
/// </remarks>
public static class NativeFormat
{
    public const string Extension = ".mtk";

    internal const string FormatId = "modeling-toolkit";
    internal const int Version = 1;
    internal const string Units = "mm";
    internal const string UpAxis = "Z";
    internal const string ManifestEntry = "manifest.json";
}
