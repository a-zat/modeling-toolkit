using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class OcctKernelTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void Info_reports_the_OpenCASCADE_version()
    {
        Assert.Equal("OpenCASCADE", _kernel.Info.Name);
        Assert.Matches(@"^\d+\.\d+\.\d+", _kernel.Info.Version);
    }

    [Fact]
    public void Box_volume_is_the_product_of_its_dimensions()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);

        Assert.Equal(6000, _kernel.ComputeVolume(box), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MakeBox_rejects_invalid_dimensions_with_the_native_error_message(double size)
    {
        var exception = Assert.Throws<ArgumentException>(() => _kernel.MakeBox(size, 1, 1));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void Primitives_have_the_volumes_of_their_formulas()
    {
        using IKernelShape cylinder = _kernel.MakeCylinder(10, 40);
        using IKernelShape sphere = _kernel.MakeSphere(25);
        using IKernelShape cone = _kernel.MakeCone(30, 10, 60);
        using IKernelShape torus = _kernel.MakeTorus(50, 10);

        Assert.Equal(Math.PI * 10 * 10 * 40, _kernel.ComputeVolume(cylinder), precision: 3);
        Assert.Equal(4.0 / 3 * Math.PI * 25 * 25 * 25, _kernel.ComputeVolume(sphere), precision: 3);
        Assert.Equal(Math.PI * 60 / 3 * (30 * 30 + 30 * 10 + 10 * 10), _kernel.ComputeVolume(cone), precision: 3);
        Assert.Equal(2 * Math.PI * Math.PI * 50 * 10 * 10, _kernel.ComputeVolume(torus), precision: 3);
    }

    [Fact]
    public void Primitives_stand_where_the_kernel_documents()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);
        using IKernelShape cylinder = _kernel.MakeCylinder(10, 40);
        using IKernelShape sphere = _kernel.MakeSphere(25);
        using IKernelShape cone = _kernel.MakeCone(30, 10, 60);
        using IKernelShape torus = _kernel.MakeTorus(50, 10);

        AssertBox(new Point3d(0, 0, 0), new Point3d(10, 20, 30), _kernel.GetBoundingBox(box));
        AssertBox(new Point3d(-10, -10, 0), new Point3d(10, 10, 40), _kernel.GetBoundingBox(cylinder));
        AssertBox(new Point3d(-25, -25, -25), new Point3d(25, 25, 25), _kernel.GetBoundingBox(sphere));
        AssertBox(new Point3d(-30, -30, 0), new Point3d(30, 30, 60), _kernel.GetBoundingBox(cone));
        AssertBox(new Point3d(-60, -60, -10), new Point3d(60, 60, 10), _kernel.GetBoundingBox(torus));
    }

    [Fact]
    public void A_placed_shape_is_moved_and_the_original_is_not()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);
        // A quarter turn about Z, then a translation: (x, y, z) -> (-y + 100, x + 200, z + 300).
        var placement = new Transform(
            0, -1, 0, 100,
            1, 0, 0, 200,
            0, 0, 1, 300);

        using IKernelShape placed = _kernel.Place(box, placement);

        AssertBox(new Point3d(80, 200, 300), new Point3d(100, 210, 330), _kernel.GetBoundingBox(placed));
        Assert.Equal(6000, _kernel.ComputeVolume(placed), precision: 6);
        AssertBox(new Point3d(0, 0, 0), new Point3d(10, 20, 30), _kernel.GetBoundingBox(box));
    }

    [Theory]
    [InlineData(2, 0, 0, 0, 2, 0, 0, 0, 2)] // scaling
    [InlineData(1, 0, 0, 0, 1, 0, 0, 0, -1)] // reflection
    [InlineData(1, 1, 0, 0, 1, 0, 0, 0, 1)] // shearing
    public void Placements_that_are_not_rigid_are_rejected(
        double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
    {
        using IKernelShape box = _kernel.MakeBox(1, 1, 1);

        var exception = Assert.Throws<ArgumentException>(
            () => _kernel.Place(box, new Transform(m00, m01, m02, 0, m10, m11, m12, 0, m20, m21, m22, 0)));

        Assert.Contains("rotation", exception.Message);
    }

    [Fact]
    public void Primitives_reject_invalid_dimensions_with_the_native_error_message()
    {
        Assert.Contains("Cylinder", Assert.Throws<ArgumentException>(() => _kernel.MakeCylinder(0, 1)).Message);
        Assert.Contains("Sphere", Assert.Throws<ArgumentException>(() => _kernel.MakeSphere(-1)).Message);
        Assert.Contains("cylinder", Assert.Throws<ArgumentException>(() => _kernel.MakeCone(2, 2, 1)).Message);
        Assert.Contains("minor", Assert.Throws<ArgumentException>(() => _kernel.MakeTorus(2, 3)).Message);
    }

    [Fact]
    public void A_box_is_tessellated_into_twelve_triangles_facing_outwards()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);

        MeshEntity mesh = _kernel.Tessellate(box, linearDeflection: 1).Mesh;
        Measures measures = Measure(mesh);

        Assert.Equal(24, mesh.Vertices.Length); // each face has its own vertices
        Assert.Equal(12, mesh.Triangles.Length);
        Assert.Equal(2200, measures.Area, precision: 6);
        Assert.Equal(6000, measures.Volume, precision: 6);
        Assert.Equal(0, measures.TrianglesAgainstTheirNormals);
        Assert.All(mesh.Normals!, normal => Assert.Equal(1f, normal.Length(), precision: 4));
        Assert.Null(mesh.TextureCoordinates);
        Assert.Null(mesh.Material);
    }

    [Fact]
    public void Curved_solids_are_tessellated_close_to_their_surfaces_from_the_inside()
    {
        (string Name, IKernelShape Shape, double Area, double Volume)[] cases =
        [
            ("cylinder", _kernel.MakeCylinder(10, 40), 2 * Math.PI * 10 * (10 + 40), Math.PI * 10 * 10 * 40),
            ("sphere", _kernel.MakeSphere(25), 4 * Math.PI * 25 * 25, 4.0 / 3 * Math.PI * 25 * 25 * 25),
            (
                "cone",
                _kernel.MakeCone(30, 10, 60),
                Math.PI * (30 + 10) * Math.Sqrt(60 * 60 + 20 * 20) + Math.PI * (30 * 30 + 10 * 10),
                Math.PI * 60 / 3 * (30 * 30 + 30 * 10 + 10 * 10)),
            ("torus", _kernel.MakeTorus(50, 10), 4 * Math.PI * Math.PI * 50 * 10, 2 * Math.PI * Math.PI * 50 * 10 * 10),
        ];

        foreach ((string name, IKernelShape shape, double area, double volume) in cases)
        {
            using (shape)
            {
                Measures measures = Measure(_kernel.Tessellate(shape, linearDeflection: 0.01, angularDeflection: 0.2).Mesh);

                Assert.True(Math.Abs(measures.Area - area) < 0.005 * area, $"{name}: area {measures.Area} of {area}");
                Assert.True(
                    Math.Abs(measures.Volume - volume) < 0.005 * volume,
                    $"{name}: volume {measures.Volume} of {volume}");
                Assert.True(measures.Volume < volume, $"{name}: the triangles stay inside the surface");
                Assert.Equal(0, measures.TrianglesAgainstTheirNormals);
            }
        }
    }

    [Fact]
    public void A_finer_deflection_gives_more_triangles_whatever_was_tessellated_before()
    {
        using IKernelShape sphere = _kernel.MakeSphere(25);
        using IKernelShape fresh = _kernel.MakeSphere(25);

        int coarse = _kernel.Tessellate(fresh, 1).Mesh.Triangles.Length;
        int fine = _kernel.Tessellate(sphere, 0.01).Mesh.Triangles.Length;
        int coarseAgain = _kernel.Tessellate(sphere, 1).Mesh.Triangles.Length;

        Assert.True(fine > 10 * coarse);
        Assert.Equal(coarse, coarseAgain);
    }

    [Fact]
    public void A_placed_shape_is_tessellated_where_it_stands()
    {
        using IKernelShape box = _kernel.MakeBox(10, 20, 30);
        using IKernelShape placed = _kernel.Place(box, new Transform(0, -1, 0, 100, 1, 0, 0, 200, 0, 0, 1, 300));

        MeshEntity mesh = _kernel.Tessellate(placed, 1).Mesh;

        BoundingBox box3 = mesh.Vertices.Aggregate(BoundingBox.Empty, (box, vertex) => box.Include(vertex));
        AssertBox(new Point3d(80, 200, 300), new Point3d(100, 210, 330), box3);
        Assert.Equal(6000, Measure(mesh).Volume, precision: 6);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(-1, 0.5)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(1, 0)]
    [InlineData(1, 3.2)]
    [InlineData(1, double.NaN)]
    public void Tessellation_rejects_invalid_deflections(double linear, double angular)
    {
        using IKernelShape box = _kernel.MakeBox(1, 1, 1);

        var exception = Assert.Throws<ArgumentException>(() => _kernel.Tessellate(box, linear, angular));

        Assert.Contains("deflection", exception.Message);
    }

    [Fact]
    public void Overlapping_boxes_combine_into_the_volumes_of_the_set_operations()
    {
        using IKernelShape a = _kernel.MakeBox(10, 10, 10);
        using IKernelShape b = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(5, 0, 0));

        using IKernelShape union = _kernel.Union(a, b);
        using IKernelShape common = _kernel.Intersect(a, b);
        using IKernelShape difference = _kernel.Subtract(a, b);

        Assert.Equal(1500, _kernel.ComputeVolume(union), precision: 6);
        Assert.Equal(500, _kernel.ComputeVolume(common), precision: 6);
        Assert.Equal(500, _kernel.ComputeVolume(difference), precision: 6);
        AssertBox(new Point3d(0, 0, 0), new Point3d(15, 10, 10), _kernel.GetBoundingBox(union));
        AssertBox(new Point3d(5, 0, 0), new Point3d(10, 10, 10), _kernel.GetBoundingBox(common));
        AssertBox(new Point3d(0, 0, 0), new Point3d(5, 10, 10), _kernel.GetBoundingBox(difference));
        Assert.Equal(1000, _kernel.ComputeVolume(a), precision: 6);
    }

    [Fact]
    public void A_cylinder_subtracted_from_a_box_leaves_a_hole()
    {
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);
        using IKernelShape drill = _kernel.Place(_kernel.MakeCylinder(20, 120), Transform.Translation(50, 50, -10));

        using IKernelShape drilled = _kernel.Subtract(box, drill);

        Assert.Equal(1e6 - Math.PI * 20 * 20 * 100, _kernel.ComputeVolume(drilled), precision: 2);
    }

    [Fact]
    public void An_empty_result_is_reported_as_an_error()
    {
        using IKernelShape a = _kernel.MakeBox(10, 10, 10);
        using IKernelShape far = _kernel.Place(_kernel.MakeBox(10, 10, 10), Transform.Translation(100, 0, 0));

        var intersection = Assert.Throws<GeometryKernelException>(() => _kernel.Intersect(a, far));
        var difference = Assert.Throws<GeometryKernelException>(() => _kernel.Subtract(a, a));

        Assert.Contains("empty", intersection.Message);
        Assert.Contains("empty", difference.Message);
    }

    [Fact]
    public void Tessellation_gives_the_triangles_of_each_face_and_the_edges()
    {
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);
        using IKernelShape drill = _kernel.Place(_kernel.MakeCylinder(20, 120), Transform.Translation(50, 50, -10));
        using IKernelShape drilled = _kernel.Subtract(box, drill);

        ShapeTessellation tessellation = _kernel.Tessellate(drilled, 0.1);

        Assert.Equal(7, tessellation.FaceCount); // the six sides of the box and the hole
        Assert.Equal(0, tessellation.TrianglesOf(0).Start.Value);
        Assert.Equal(tessellation.Mesh.Triangles.Length, tessellation.TrianglesOf(6).End.Value);
        Assert.Equal(14, tessellation.Edges.Count); // those of the box and the two circles of the hole
        for (int edge = 0; edge < tessellation.Edges.Count; edge++)
        {
            foreach (Point3d point in tessellation.Edges[edge])
            {
                Assert.InRange(point.X, -1e-9, 100 + 1e-9);
                Assert.InRange(point.Z, -1e-9, 100 + 1e-9);
            }
        }
    }

    [Fact]
    public void Shapes_can_be_used_from_several_threads_at_the_same_time()
    {
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);
        using IKernelShape drill = _kernel.Place(_kernel.MakeCylinder(20, 120), Transform.Translation(50, 50, -10));
        using IKernelShape drilled = _kernel.Subtract(box, drill);
        int triangles = _kernel.Tessellate(drilled, 0.1).Mesh.Triangles.Length;
        double volume = _kernel.ComputeVolume(drilled);

        // The same shape tessellated, measured, written and cut on many threads at once.
        var results = new System.Collections.Concurrent.ConcurrentBag<(int Triangles, double Volume, double Cut)>();
        Parallel.For(0, 32, _ =>
        {
            using IKernelShape cut = _kernel.Subtract(box, drill);
            Assert.NotEmpty(_kernel.Serialize(drilled));
            results.Add((_kernel.Tessellate(drilled, 0.1).Mesh.Triangles.Length, _kernel.ComputeVolume(drilled),
                _kernel.ComputeVolume(cut)));
        });

        Assert.All(results, result =>
        {
            Assert.Equal(triangles, result.Triangles);
            Assert.Equal(volume, result.Volume, precision: 6);
            Assert.Equal(volume, result.Cut, precision: 6);
        });
    }

    [Fact]
    public void A_disposed_shape_cannot_be_used()
    {
        IKernelShape box = _kernel.MakeBox(1, 1, 1);
        box.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _kernel.ComputeVolume(box));
        Assert.Throws<ObjectDisposedException>(() => _kernel.GetBoundingBox(box));
        Assert.Throws<ObjectDisposedException>(() => _kernel.Place(box, Transform.Identity));
        Assert.Throws<ObjectDisposedException>(() => _kernel.Tessellate(box, 1));
        using IKernelShape other = _kernel.MakeBox(1, 1, 1);
        Assert.Throws<ObjectDisposedException>(() => _kernel.Union(box, other));
        Assert.Throws<ObjectDisposedException>(() => _kernel.Subtract(other, box));
    }

    private readonly record struct Measures(double Area, double Volume, int TrianglesAgainstTheirNormals);

    // The area of the surface and the volume the triangles enclose (negative when they face inwards), and how many
    // have their front, from where the vertices run counter-clockwise, opposite to the normals of their vertices.
    private static Measures Measure(MeshEntity mesh)
    {
        double area = 0;
        double volume = 0;
        int against = 0;
        foreach (Triangle triangle in mesh.Triangles)
        {
            Point3d a = mesh.Vertices[triangle.A];
            Point3d b = mesh.Vertices[triangle.B];
            Point3d c = mesh.Vertices[triangle.C];
            (double X, double Y, double Z) facing = Cross(Minus(b, a), Minus(c, a));
            double triangleArea = 0.5 * Math.Sqrt(Dot(facing, facing));
            area += triangleArea;
            volume += Dot((a.X, a.Y, a.Z), Cross((b.X, b.Y, b.Z), (c.X, c.Y, c.Z))) / 6.0;

            double alongNormals = 0;
            foreach (int vertex in (ReadOnlySpan<int>)[triangle.A, triangle.B, triangle.C])
            {
                Vector3 normal = mesh.Normals![vertex];
                alongNormals += Dot(facing, (normal.X, normal.Y, normal.Z));
            }

            // The poles of a sphere leave triangles of no area, whose front is not defined.
            if (triangleArea > 1e-9 && alongNormals <= 0)
            {
                against++;
            }
        }
        return new Measures(area, volume, against);
    }

    private static (double X, double Y, double Z) Minus(Point3d a, Point3d b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (double X, double Y, double Z) Cross(
        (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static void AssertBox(Point3d min, Point3d max, BoundingBox actual)
    {
        const double tolerance = 1e-6;
        Assert.Equal(min.X, actual.Min.X, tolerance);
        Assert.Equal(min.Y, actual.Min.Y, tolerance);
        Assert.Equal(min.Z, actual.Min.Z, tolerance);
        Assert.Equal(max.X, actual.Max.X, tolerance);
        Assert.Equal(max.Y, actual.Max.Y, tolerance);
        Assert.Equal(max.Z, actual.Max.Z, tolerance);
    }
}
