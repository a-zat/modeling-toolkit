using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// A shape turned into triangles (see <see cref="IGeometryKernel.Tessellate"/>): one mesh for all its faces, with the
/// triangles of each face one after the other, and its edges as polylines.
/// </summary>
public sealed class ShapeTessellation
{
    private readonly int[] _firstTriangles;

    /// <param name="firstTriangles">
    /// The first triangle of each face, then the number of triangles: one more value than there are faces.
    /// </param>
    /// <exception cref="ArgumentException">The first triangles are not in order or out of range.</exception>
    public ShapeTessellation(MeshEntity mesh, int[] firstTriangles, Polylines edges)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(firstTriangles);
        ArgumentNullException.ThrowIfNull(edges);
        if (firstTriangles.Length == 0 || firstTriangles[0] != 0 || firstTriangles[^1] != mesh.Triangles.Length ||
            firstTriangles.Zip(firstTriangles.Skip(1)).Any(pair => pair.First > pair.Second))
        {
            throw new ArgumentException(
                "The first triangles must go up from 0 to the number of triangles.", nameof(firstTriangles));
        }

        Mesh = mesh;
        _firstTriangles = firstTriangles;
        Edges = edges;
    }

    public MeshEntity Mesh { get; }

    /// <summary>The faces, in the order of the shape; a face may have no triangles.</summary>
    public int FaceCount => _firstTriangles.Length - 1;

    /// <summary>
    /// The edges, each once however many faces it bounds, without the seams of closed surfaces and the degenerate
    /// edges, such as the poles of a sphere.
    /// </summary>
    public Polylines Edges { get; }

    /// <summary>The triangles of a face in <see cref="Mesh"/>.</summary>
    public Range TrianglesOf(int face)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, FaceCount);
        return _firstTriangles[face].._firstTriangles[face + 1];
    }
}
