using System.Numerics;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>Triangle mesh. The entity owns the arrays it is created with.</summary>
public sealed class MeshEntity : Entity
{
    /// <summary>
    /// The material of all the triangles of the mesh, which is one surface: a mesh has no faces of its own. Null for
    /// the default material.
    /// </summary>
    public Material? Material
    {
        get;
        set
        {
            if (!ReferenceEquals(field, value))
            {
                field = value;
                Raise(new PropertyChanged(this, nameof(Material)));
            }
        }
    }

    public MeshEntity(
        Point3d[] vertices,
        Triangle[] triangles,
        Vector3[]? normals = null,
        Vector2[]? textureCoordinates = null)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        if (normals is not null && normals.Length != vertices.Length)
        {
            throw new ArgumentException("A mesh needs one normal per vertex.", nameof(normals));
        }
        if (textureCoordinates is not null && textureCoordinates.Length != vertices.Length)
        {
            throw new ArgumentException("A mesh needs one texture coordinate per vertex.", nameof(textureCoordinates));
        }
        foreach (Triangle triangle in triangles)
        {
            if ((uint)triangle.A >= (uint)vertices.Length ||
                (uint)triangle.B >= (uint)vertices.Length ||
                (uint)triangle.C >= (uint)vertices.Length)
            {
                throw new ArgumentException($"{triangle} refers to a vertex that does not exist.", nameof(triangles));
            }
        }

        Vertices = vertices;
        Triangles = triangles;
        Normals = normals;
        TextureCoordinates = textureCoordinates;
    }

    /// <summary>Vertex positions, in the coordinates of the containing block.</summary>
    public Point3d[] Vertices { get; }

    public Triangle[] Triangles { get; }

    /// <summary>Unit normals, one per vertex; null when the mesh has none.</summary>
    public Vector3[]? Normals { get; }

    /// <summary>
    /// Texture coordinates, one per vertex; (0, 0) is the bottom-left corner of the image and (1, 1) the top-right one.
    /// Null when the mesh has none.
    /// </summary>
    public Vector2[]? TextureCoordinates { get; }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes)
    {
        BoundingBox box = BoundingBox.Empty;
        foreach (Point3d vertex in Vertices)
        {
            box = box.Include(vertex);
        }
        return box;
    }
}
