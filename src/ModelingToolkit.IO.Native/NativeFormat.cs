namespace ModelingToolkit.IO.Native;

/// <summary>
/// The native file format: a zip archive holding <c>manifest.json</c> (textures, materials, blocks and their
/// entities), one binary file per mesh, one file per B-rep shape and the textures as encoded images.
/// </summary>
/// <remarks>
/// A mesh file contains, little-endian and in this order: the vertices (3 doubles each), the triangles (3 int32
/// each), then, when the manifest says so, the normals (3 floats each) and the texture coordinates (2 floats each).
/// A shape file is the shape as the geometry kernel writes it, in the BRep text format of OpenCASCADE.
/// <para>
/// The manifest holds the version of the format the file was written in; files are always written in the current
/// one. The files of older versions are read, and migrated if needed, those of newer or unknown versions refused. A
/// change that older readers would misread needs a new version. Version 2 added the shapes, so the files of version
/// 1 are read as they are.
/// </para>
/// </remarks>
public static class NativeFormat
{
    public const string Extension = ".mtk";

    internal const string FormatId = "modeling-toolkit";
    internal const int Version = 2;
    internal const string Units = "mm";
    internal const string UpAxis = "Z";
    internal const string ManifestEntry = "manifest.json";
}
