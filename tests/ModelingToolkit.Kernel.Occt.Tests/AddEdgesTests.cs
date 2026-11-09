using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class AddEdgesTests
{
    private readonly OcctKernel _kernel = new();

    private static LineCurve[] Rectangle(double x0, double y0, double x1, double y1, double z = 0)
    {
        Point3d a = new(x0, y0, z), b = new(x1, y0, z), c = new(x1, y1, z), d = new(x0, y1, z);
        return [new(a, b), new(b, c), new(c, d), new(d, a)];
    }

    [Fact]
    public void Lines_that_close_a_loop_on_nothing_make_a_face()
    {
        ShapeEdit edit = _kernel.AddEdges(null, Rectangle(0, 0, 100, 60));
        using IKernelShape shape = edit.Shape;

        Assert.Equal(new ShapeTopology(0, 1, 4, 4), _kernel.CountElements(shape));
        Assert.Equal([-1], edit.FaceOrigins);
        Assert.Equal(6000, _kernel.GetFaceInfo(shape, 0).Area, precision: 6);
    }

    [Fact]
    public void Lines_that_close_a_loop_beside_a_slanted_face_without_touching_it_make_a_face()
    {
        // The box is turned, so that the box of its top face holds the loop; the loop is 40 mm off that face, which
        // is where the loop falls on it when seen along its normal.
        using IKernelShape box = _kernel.Place(_kernel.MakeBox(200, 200, 100),
            Transform.Rotation(Point3d.Origin, new Vector3d(0, 1, 0), Math.PI / 4));
        Plane slanted = Enumerable.Range(0, 6).Select(face => _kernel.GetFaceInfo(box, face).Plane!.Value)
            .First(plane => Math.Abs(plane.Normal.X) > 0.1 && Math.Abs(plane.Normal.Z) > 0.1);
        BoundingBox bounds = _kernel.GetBoundingBox(box);
        Point3d middle = new((bounds.Min.X + bounds.Max.X) / 2, (bounds.Min.Y + bounds.Max.Y) / 2,
                             (bounds.Min.Z + bounds.Max.Z) / 2);
        double inside = slanted.Normal.X * middle.X + slanted.Normal.Y * middle.Y + slanted.Normal.Z * middle.Z -
                        slanted.Offset;
        Vector3d out40 = slanted.Normal * (40 - inside);
        Point3d at = new(middle.X + out40.X, middle.Y + out40.Y, middle.Z + out40.Z);
        ShapeEdit edit = _kernel.AddEdges(box, Rectangle(at.X - 10, at.Y - 10, at.X + 10, at.Y + 10, at.Z));
        using IKernelShape shape = edit.Shape;

        Assert.Equal(new ShapeTopology(1, 7, 16, 12), _kernel.CountElements(shape));
        Assert.Equal(-1, edit.FaceOrigins[^1]);
    }

    [Fact]
    public void A_line_across_a_painted_face_leaves_both_parts_painted_until_undone()
    {
        var document = new Document();
        var entity = new BrepEntity(_kernel, _kernel.AddEdges(null, Rectangle(0, 0, 100, 100)).Shape);
        document.Root.Entities.Add(entity);
        var red = new Material("Red");
        entity.Paint([0], red);
        var session = new EditSession(document);

        ShapeEdit edit = _kernel.AddEdges(entity.Shape, [new LineCurve(new(0, 50, 0), new(100, 50, 0))]);
        session.Execute(new ReplaceShapeCommand("Line", entity, edit));

        Assert.Equal(2, _kernel.CountElements(entity.Shape).Faces);
        Assert.Same(red, entity.MaterialOf(0));
        Assert.Same(red, entity.MaterialOf(1));
        session.Undo();
        Assert.Equal(1, _kernel.CountElements(entity.Shape).Faces);
        Assert.Equal([0], entity.FaceMaterials.Keys);
    }

    [Fact]
    public void A_rectangle_on_a_box_splits_its_top_and_keeps_the_solid()
    {
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);

        ShapeEdit edit = _kernel.AddEdges(box, Rectangle(20, 20, 80, 80, 100));
        using IKernelShape shape = edit.Shape;

        Assert.Equal(new ShapeTopology(1, 7, 16, 12), _kernel.CountElements(shape));
        Assert.Equal(1e6, _kernel.ComputeVolume(shape), precision: 3);
        Assert.Single(edit.FaceOrigins.GroupBy(origin => origin), group => group.Count() == 2);
        Assert.DoesNotContain(-1, edit.FaceOrigins);
    }

    [Fact]
    public void Edges_are_described_for_the_inference()
    {
        using IKernelShape shape = _kernel.AddEdges(null,
        [
            new ArcCurve(new(20, 0, 0), new(0, 20, 0), new(-20, 0, 0)),
            new LineCurve(new(-20, 0, 0), new(20, 0, 0)),
        ]).Shape;

        EdgeInfo[] edges = [.. Enumerable.Range(0, _kernel.CountElements(shape).Edges).Select(e => _kernel.GetEdgeInfo(shape, e))];

        EdgeInfo arc = Assert.Single(edges, edge => edge.Kind == CurveKind.Arc);
        Assert.Equal(Math.PI * 20, arc.Length, precision: 9);
        Assert.Equal(20, Assert.NotNull(arc.Circle).Radius, precision: 9);
        EdgeInfo line = Assert.Single(edges, edge => edge.Kind == CurveKind.Line);
        Assert.Null(line.Circle);
        Assert.Equal(0, line.Middle.X, precision: 9);
        Assert.Equal(1, _kernel.CountElements(shape).Faces); // the half disc
    }

    [Fact]
    public void Lines_without_faces_are_tessellated_into_their_edges_only()
    {
        using IKernelShape shape = _kernel.AddEdges(null,
            [new LineCurve(new(0, 0, 0), new(100, 0, 0)), new LineCurve(new(100, 0, 0), new(100, 50, 0))]).Shape;

        ShapeTessellation tessellation = _kernel.Tessellate(shape, 0.1);

        Assert.Empty(tessellation.Mesh.Triangles);
        Assert.Equal(2, tessellation.Edges.Count);
        Assert.Equal(3, tessellation.Vertices.Length);
    }

    [Fact]
    public void Degenerate_curves_and_no_curves_are_refused()
    {
        Assert.Throws<ArgumentException>(() => _kernel.AddEdges(null, []));
        Assert.Throws<ArgumentException>(() => _kernel.AddEdges(null, [new LineCurve(new(1, 1, 1), new(1, 1, 1))]));
        Assert.Throws<ArgumentException>(() => _kernel.AddEdges(null, [new CircleCurve(new(0, 0, 0), new(0, 0, 0), 5)]));
    }
}
