using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class MeshEntityTests
{
    private static readonly Point3d[] Vertices = [new(0, 0, 0), new(4, 0, 0), new(0, 3, 2)];

    [Fact]
    public void Bounding_box_contains_every_vertex()
    {
        var mesh = new MeshEntity(Vertices, [new Triangle(0, 1, 2)]);

        Assert.Equal(new BoundingBox(new Point3d(0, 0, 0), new Point3d(4, 3, 2)), mesh.GetBoundingBox());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public void Triangles_must_refer_to_existing_vertices(int index)
    {
        Assert.Throws<ArgumentException>(() => new MeshEntity(Vertices, [new Triangle(0, 1, index)]));
    }

    [Fact]
    public void Normals_and_texture_coordinates_need_one_value_per_vertex()
    {
        Triangle[] triangles = [new(0, 1, 2)];

        Assert.Throws<ArgumentException>(() => new MeshEntity(Vertices, triangles, normals: [Vector3.UnitZ]));
        Assert.Throws<ArgumentException>(() => new MeshEntity(Vertices, triangles, textureCoordinates: [Vector2.Zero]));
    }
}
