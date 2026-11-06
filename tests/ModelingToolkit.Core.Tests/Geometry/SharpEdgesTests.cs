using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Tests.Geometry;

public sealed class SharpEdgesTests
{
    [Fact]
    public void A_box_whose_faces_have_their_own_vertices_has_its_twelve_edges_and_no_diagonals()
    {
        (Point3d[] vertices, Triangle[] triangles) = Box();

        Polylines edges = SharpEdges.Of(vertices, triangles);

        Assert.Equal(12, edges.Count);
        for (int index = 0; index < edges.Count; index++)
        {
            ReadOnlySpan<Point3d> edge = edges[index];
            Assert.Equal(2, edge.Length);
            // Along an axis: the ends differ in one coordinate only.
            int differing = (edge[0].X != edge[1].X ? 1 : 0) + (edge[0].Y != edge[1].Y ? 1 : 0) +
                            (edge[0].Z != edge[1].Z ? 1 : 0);
            Assert.Equal(1, differing);
        }
    }

    [Fact]
    public void A_flat_square_has_its_border_and_no_diagonal()
    {
        Point3d[] vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];

        Polylines edges = SharpEdges.Of(vertices, [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]);

        Assert.Equal(4, edges.Count);
    }

    [Theory]
    [InlineData(10, 4)] // gently folded: only the border
    [InlineData(45, 5)] // folded: the border and the fold
    public void A_fold_is_an_edge_beyond_the_angle(double degrees, int expected)
    {
        // Two triangles sharing the edge from (0, 0, 0) to (0, 1, 0), the second turned about it.
        double angle = degrees * Math.PI / 180;
        Point3d[] vertices = [new(0, 0, 0), new(0, 1, 0), new(-1, 0, 0), new(Math.Cos(angle), 0, Math.Sin(angle))];

        Polylines edges = SharpEdges.Of(vertices, [new Triangle(0, 1, 2), new Triangle(1, 0, 3)]);

        Assert.Equal(expected, edges.Count);
    }

    [Fact]
    public void Triangles_without_area_have_no_edges()
    {
        Point3d[] vertices = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(0, 0, 0)];

        Polylines edges = SharpEdges.Of(vertices, [new Triangle(0, 1, 2), new Triangle(0, 1, 3)]);

        Assert.Equal(0, edges.Count);
    }

    // A box of 1 × 2 × 3 as meshes of files have it: 24 vertices, four per face, and two triangles per face.
    private static (Point3d[] Vertices, Triangle[] Triangles) Box()
    {
        var vertices = new List<Point3d>();
        var triangles = new List<Triangle>();
        double[] size = [1, 2, 3];
        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = 0; side < 2; side++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3, first = vertices.Count;
                foreach ((int i, int j) in new[] { (0, 0), (1, 0), (1, 1), (0, 1) })
                {
                    double[] point = new double[3];
                    point[axis] = side * size[axis];
                    point[u] = i * size[u];
                    point[v] = j * size[v];
                    vertices.Add(new Point3d(point[0], point[1], point[2]));
                }
                // Counter-clockwise seen from outside.
                triangles.AddRange(side == 1
                    ? [new Triangle(first, first + 1, first + 2), new Triangle(first, first + 2, first + 3)]
                    : [new Triangle(first, first + 2, first + 1), new Triangle(first, first + 3, first + 2)]);
            }
        }
        return ([.. vertices], [.. triangles]);
    }
}
