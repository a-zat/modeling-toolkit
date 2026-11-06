using System.Numerics;
using System.Runtime.InteropServices;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Interop;

internal static unsafe class MeshInfoExtensions
{
    /// <summary>A mesh of the domain model with a copy of the data of a native mesh, without its material.</summary>
    public static MeshEntity ToMeshEntity(this in MeshInfo mesh)
    {
        int vertexCount = mesh.VertexCount;

        // The native arrays are flat (x, y, z, x, y, z...): reinterpreted as structs, they are copied in one go.
        Point3d[] vertices = Copy<double, Point3d>(mesh.Vertices, 3 * vertexCount)!;
        Triangle[] triangles = Copy<int, Triangle>(mesh.Triangles, 3 * mesh.TriangleCount)!;
        Vector3[]? normals = Copy<float, Vector3>(mesh.Normals, 3 * vertexCount);
        Vector2[]? textureCoordinates = Copy<float, Vector2>(mesh.TextureCoordinates, 2 * vertexCount);

        return new MeshEntity(vertices, triangles, normals, textureCoordinates);
    }

    /// <summary>Copies <paramref name="count"/> native values as an array of structs; null if the data is absent.</summary>
    private static TResult[]? Copy<TValue, TResult>(TValue* values, int count)
        where TValue : unmanaged
        where TResult : unmanaged =>
        values == null ? null : MemoryMarshal.Cast<TValue, TResult>(new ReadOnlySpan<TValue>(values, count)).ToArray();
}
