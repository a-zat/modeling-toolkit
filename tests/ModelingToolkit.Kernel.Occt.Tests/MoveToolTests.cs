using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class MoveToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly MoveTool _tool;

    public MoveToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool = new MoveTool(_kernel);
        _tool.Activate(_host);
    }

    private static ToolInput Ground(double x, double y, ToolModifiers modifiers = ToolModifiers.None) =>
        new(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)), modifiers);

    private static ToolInput On(Entity entity, Point3d point) =>
        new(new ToolPick([entity], null, point, Vector3d.UnitZ,
            new PointerRay(point + new Vector3d(0, 0, 1000), new Vector3d(0, 0, -1), 1, 0)));

    [Fact]
    public void A_shape_clicked_moves_to_the_point_clicked_with_its_materials()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100));
        var red = new Material("Red");
        box.Paint([0], red);
        _document.Root.Entities.Add(box);

        _tool.Click(On(box, new Point3d(50, 50, 100)));
        _tool.Hover(Ground(550, 50));
        Assert.Equal("500 mm", _tool.Measurement);
        Assert.Equal(13, _host.Preview.Lines.Count); // the move and the box
        _tool.Click(Ground(550, 50));

        Assert.Equal(new BoundingBox(new Point3d(500, 0, 0), new Point3d(600, 100, 100)), Rounded(box.GetBoundingBox()));
        Assert.Same(red, box.MaterialOf(0));
        _host.Session.Undo();
        Assert.Equal(new Point3d(0, 0, 0), Rounded(box.GetBoundingBox()).Min);
    }

    [Fact]
    public void A_move_nearly_along_an_axis_snaps_to_it()
    {
        var block = new Block("Part");
        block.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)));
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        _document.Root.Entities.Add(reference);
        _host.SelectedPath = [reference];

        _tool.Click(Ground(0, 0));
        // 60 mm off the X axis 1 m along it: further than the pointer snaps by, but within the angle of the axes.
        _tool.Hover(Ground(1000, 60));
        Assert.Equal(InferenceKind.AxisX, _host.Preview.Lines[0].Along);
        _tool.Click(Ground(1000, 60));

        Assert.Equal(new Point3d(1000, 0, 0), reference.Transform.Apply(Point3d.Origin));
    }

    [Fact]
    public void With_Option_a_copy_of_the_selection_moves_by_the_distance_typed()
    {
        var block = new Block("Part");
        block.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)));
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        _document.Root.Entities.Add(reference);
        _host.SelectedPath = [reference];

        _tool.Click(Ground(0, 0));
        _tool.Hover(Ground(0, 30, ToolModifiers.Alt));
        Assert.True(_tool.TypeMeasurement("200"));

        Assert.Equal(2, _document.Root.Entities.Count);
        Assert.Equal(Transform.Identity, reference.Transform);
        var copy = (BlockReference)_document.Root.Entities[1];
        Assert.Same(block, copy.Block);
        Assert.Equal(new Point3d(0, 200, 0), copy.Transform.Apply(Point3d.Origin));
    }

    [Fact]
    public void A_mesh_moves_in_place_and_nothing_moves_without_a_target()
    {
        var mesh = new MeshEntity([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [new Triangle(0, 1, 2)]) { Name = "Tri" };
        _document.Root.Entities.Add(mesh);
        _tool.Click(Ground(0, 0));
        Assert.Single(_host.Reports);

        _tool.Click(On(mesh, new Point3d(0, 0, 0)));
        _tool.Click(Ground(50, 50));

        var moved = Assert.IsType<MeshEntity>(Assert.Single(_document.Root.Entities));
        Assert.Equal("Tri", moved.Name);
        Assert.Equal(new Point3d(50, 50, 0), moved.Vertices[0]);
    }

    private static BoundingBox Rounded(BoundingBox box) => new(Round(box.Min), Round(box.Max));

    private static Point3d Round(Point3d point) => new(Math.Round(point.X, 6), Math.Round(point.Y, 6), Math.Round(point.Z, 6));
}
