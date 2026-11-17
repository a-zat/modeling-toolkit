using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Meshes;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Meshes;

public sealed class MeshSimplifierTests
{
    // A square of a side, of cells × cells squares of two triangles, facing up, with texture coordinates.
    internal static MeshEntity Grid(int cells, double side = 1000, double z = 0)
    {
        var vertices = new List<Point3d>();
        var uvs = new List<Vector2>();
        for (int j = 0; j <= cells; j++)
        {
            for (int i = 0; i <= cells; i++)
            {
                vertices.Add(new Point3d(side * i / cells, side * j / cells, z));
                uvs.Add(new Vector2((float)i / cells, (float)j / cells));
            }
        }
        var triangles = new List<Triangle>();
        int row = cells + 1;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                int a = j * row + i;
                triangles.Add(new Triangle(a, a + 1, a + row + 1));
                triangles.Add(new Triangle(a, a + row + 1, a + row));
            }
        }
        return new MeshEntity([.. vertices], [.. triangles], null, [.. uvs]);
    }

    // A sphere of a radius around the origin, of rings × segments, closed, each vertex once.
    internal static MeshEntity Sphere(double radius, int rings, int segments)
    {
        var vertices = new List<Point3d> { new(0, 0, radius) };
        for (int ring = 1; ring < rings; ring++)
        {
            double polar = Math.PI * ring / rings;
            for (int segment = 0; segment < segments; segment++)
            {
                double azimuth = 2 * Math.PI * segment / segments;
                vertices.Add(new Point3d(radius * Math.Sin(polar) * Math.Cos(azimuth),
                    radius * Math.Sin(polar) * Math.Sin(azimuth), radius * Math.Cos(polar)));
            }
        }
        vertices.Add(new Point3d(0, 0, -radius));
        int bottom = vertices.Count - 1;
        int At(int ring, int segment) => 1 + (ring - 1) * segments + (segment % segments);
        var triangles = new List<Triangle>();
        for (int segment = 0; segment < segments; segment++)
        {
            triangles.Add(new Triangle(0, At(1, segment), At(1, segment + 1)));
            for (int ring = 1; ring < rings - 1; ring++)
            {
                triangles.Add(new Triangle(At(ring, segment), At(ring + 1, segment), At(ring + 1, segment + 1)));
                triangles.Add(new Triangle(At(ring, segment), At(ring + 1, segment + 1), At(ring, segment + 1)));
            }
            triangles.Add(new Triangle(bottom, At(rings - 1, segment + 1), At(rings - 1, segment)));
        }
        return new MeshEntity([.. vertices], [.. triangles]);
    }

    [Fact]
    public void A_flat_grid_reduces_to_few_triangles_staying_flat_with_its_texture()
    {
        MeshEntity grid = Grid(20);

        SimplifiedMesh result = MeshSimplifier.Simplify(grid, new SimplifyOptions { KeepPercent = 5, MaxError = 0.5 });

        Assert.NotNull(result.Result);
        // Its borders kept: 80 vertices around, few inside.
        Assert.True(result.After.Triangles <= 90, $"{result.After.Triangles} triangles");
        Assert.True(result.MaxDeviation < 1e-6, $"moved {result.MaxDeviation}");
        Assert.All(result.Result!.Vertices, vertex => Assert.Equal(0, vertex.Z, 1e-9));
        // The texture follows the positions: u is x / side.
        for (int i = 0; i < result.Result.Vertices.Length; i++)
        {
            Assert.Equal(result.Result.Vertices[i].X / 1000, result.Result.TextureCoordinates![i].X, 1e-4);
        }
    }

    [Fact]
    public void A_sphere_keeps_its_shape_closely_as_it_loses_most_of_its_triangles()
    {
        MeshEntity sphere = Sphere(500, 40, 80);

        SimplifiedMesh result = MeshSimplifier.Simplify(sphere, new SimplifyOptions { KeepPercent = 20, MaxError = 0 });

        Assert.True(result.After.Triangles <= sphere.Triangles.Length / 5 + 2, $"{result.After.Triangles} triangles");
        Assert.True(result.MaxDeviation < 15, $"moved {result.MaxDeviation}");
        Assert.Equal(1, result.After.Pieces);
    }

    [Fact]
    public void The_error_limit_stops_the_reduction()
    {
        MeshEntity sphere = Sphere(500, 40, 80);

        SimplifiedMesh result = MeshSimplifier.Simplify(sphere, new SimplifyOptions { KeepPercent = 1, MaxError = 1 });

        Assert.True(result.After.Triangles > sphere.Triangles.Length / 100);
        Assert.True(result.MaxDeviation < 3, $"moved {result.MaxDeviation}");
    }

    [Fact]
    public void Small_pieces_and_repeated_triangles_are_taken_out()
    {
        MeshEntity big = Grid(2, 1000), small = Grid(1, 20, z: 500);
        Point3d[] vertices = [.. big.Vertices, .. small.Vertices];
        int offset = big.Vertices.Length;
        Triangle[] triangles =
        [
            .. big.Triangles, big.Triangles[0], new Triangle(0, 0, 1),
            .. small.Triangles.Select(t => new Triangle(t.A + offset, t.B + offset, t.C + offset)),
        ];
        var mesh = new MeshEntity(vertices, triangles);

        SimplifiedMesh result = MeshSimplifier.Simplify(mesh, new SimplifyOptions { MinPieceSize = 50 });

        Assert.Equal(2, result.Before.Pieces);
        Assert.Equal(1, result.After.Pieces);
        Assert.Equal(big.Triangles.Length, result.After.Triangles);
    }

    [Fact]
    public void Recomputed_normals_are_smooth_below_the_crease_angle_and_sharp_above()
    {
        // Two squares at right angles, along the X axis.
        var mesh = new MeshEntity(
            [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0), new(100, 0, 100), new(0, 0, 100)],
            [new(0, 1, 2), new(0, 2, 3), new(0, 5, 4), new(0, 4, 1)]);

        MeshEntity sharp = MeshSimplifier.Simplify(mesh, new SimplifyOptions { Normals = NormalsMode.Recompute, CreaseAngle = 40 }).Result!;
        MeshEntity smooth = MeshSimplifier.Simplify(mesh, new SimplifyOptions { Normals = NormalsMode.Recompute, CreaseAngle = 100 }).Result!;

        Assert.Equal(8, sharp.Vertices.Length); // the corners of the fold, once on each side
        Assert.Equal(6, smooth.Vertices.Length);
        Assert.Equal(1, SharpEdges.Of(sharp.Vertices, sharp.Triangles, sharp.Normals).Count - 6);
        Assert.Equal(0, SharpEdges.Of(smooth.Vertices, smooth.Triangles, smooth.Normals).Count - 6);
    }

    [Fact]
    public void Smoothing_moves_no_vertex_further_than_its_limit()
    {
        MeshEntity sphere = Sphere(500, 12, 24);
        Point3d[] rough = [.. sphere.Vertices.Select((v, i) => i % 2 == 0 ? v : Point3d.Origin + (v - Point3d.Origin) * 1.05)];
        var mesh = new MeshEntity(rough, sphere.Triangles);

        SimplifiedMesh result = MeshSimplifier.Simplify(mesh, new SimplifyOptions { SmoothIterations = 20, SmoothMaxMove = 4 });

        Assert.True(result.MaxDeviation <= 4 + 1e-6, $"moved {result.MaxDeviation}");
        Assert.True(result.MaxDeviation > 1);
    }
}
