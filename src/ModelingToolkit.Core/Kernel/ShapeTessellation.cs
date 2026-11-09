using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// A shape turned into triangles (see <see cref="IGeometryKernel.Tessellate"/>): one mesh for all its faces, with the
/// triangles of each face one after the other, its edges as polylines, and its vertices. Faces, edges and vertices are
/// numbered in the order of the shape, which is valid for one version of the shape.
/// </summary>
public sealed class ShapeTessellation
{
    private readonly int[] _firstTriangles;
    private readonly int[] _edgeIndices;
    private readonly Point3d[] _vertices;

    /// <param name="firstTriangles">
    /// The first triangle of each face, then the number of triangles: one more value than there are faces.
    /// </param>
    /// <param name="edgeIndices">
    /// The index in the shape of each polyline of <paramref name="edges"/>; null when they are numbered as the shape.
    /// </param>
    /// <param name="vertices">The vertices of the shape, in its order; null for none.</param>
    /// <exception cref="ArgumentException">
    /// The first triangles are not in order or out of range, or there is not one edge index per polyline.
    /// </exception>
    public ShapeTessellation(
        MeshEntity mesh, int[] firstTriangles, Polylines edges, int[]? edgeIndices = null, Point3d[]? vertices = null)
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

        if (edgeIndices is not null && edgeIndices.Length != edges.Count)
        {
            throw new ArgumentException("Each polyline needs the index of its edge.", nameof(edgeIndices));
        }

        Mesh = mesh;
        _firstTriangles = firstTriangles;
        Edges = edges;
        _edgeIndices = edgeIndices ?? [.. Enumerable.Range(0, edges.Count)];
        _vertices = vertices ?? [];
    }

    public MeshEntity Mesh { get; }

    /// <summary>The faces, in the order of the shape; a face may have no triangles.</summary>
    public int FaceCount => _firstTriangles.Length - 1;

    /// <summary>
    /// The edges, each once however many faces it bounds, without the seams of closed surfaces and the degenerate
    /// edges, such as the poles of a sphere.
    /// </summary>
    public Polylines Edges { get; }

    /// <summary>
    /// The index in the shape of each polyline of <see cref="Edges"/>: the edges left out have none, so polyline i is
    /// not always edge i.
    /// </summary>
    public ReadOnlySpan<int> EdgeIndices => _edgeIndices;

    /// <summary>All the vertices of the shape, in its order.</summary>
    public ReadOnlySpan<Point3d> Vertices => _vertices;

    /// <summary>The triangles of a face in <see cref="Mesh"/>.</summary>
    public Range TrianglesOf(int face)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(face);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(face, FaceCount);
        return _firstTriangles[face].._firstTriangles[face + 1];
    }
}
