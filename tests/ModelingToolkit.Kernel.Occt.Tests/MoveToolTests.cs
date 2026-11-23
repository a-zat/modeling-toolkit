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
    public void A_selection_dragged_by_a_corner_with_the_select_tool_lands_that_corner_on_a_corner_elsewhere()
    {
        var plate = new MeshEntity([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)],
            [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]);
        var block = new Block("Plate") { Entities = { plate } };
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        _document.Root.Entities.Add(reference);
        var floor = new MeshEntity([new(500, 300, 0), new(1000, 300, 0), new(1000, 500, 0), new(500, 500, 0)],
            [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]);
        _document.Root.Entities.Add(floor);
        _host.SelectedPath = [reference];
        var select = new SelectTool(_kernel);
        _tool.Deactivate();
        select.Activate(_host);

        // Pressed near a corner of the plate, let go near a corner of the floor: both are snapped to.
        select.Press(At([reference, plate], new Point3d(97, 98, 0), SelectTool.SelectionBody));
        select.Drag(At([floor], new Point3d(800, 400, 0), SelectTool.SelectionBody));
        select.Release(At([floor], new Point3d(997, 497, 0), SelectTool.SelectionBody));

        Assert.Equal(new Point3d(1000, 500, 0), Round(reference.Transform.Apply(new Point3d(100, 100, 0))));
        Assert.Equal(1, _host.Session.UndoCount());
    }

    private static ToolInput At(IReadOnlyList<Entity> path, Point3d point, int? handle) =>
        new(new ToolPick(path, null, point, Vector3d.UnitZ,
            new PointerRay(point + new Vector3d(0, 0, 1000), new Vector3d(0, 0, -1), 1, 0)), Handle: handle);

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
    public void A_component_turned_moves_along_its_own_axes_too()
    {
        var block = new Block("Armchair");
        block.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)));
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Rotation(Point3d.Origin, Vector3d.UnitZ, Math.PI / 4));
        _document.Root.Entities.Add(reference);
        _host.SelectedPath = [reference];

        _tool.Click(Ground(0, 0));
        // 1 m along its X axis, 30 mm off it: between the axes of the model, far from both.
        double d = 1000 / Math.Sqrt(2);
        _tool.Hover(Ground(d - 21, d + 21));
        Inference along = _host.Preview.Inference!;
        Assert.Equal((InferenceKind.AxisX, true), (along.Kind, along.OfObject));
        _tool.Click(Ground(d - 21, d + 21));

        Point3d moved = reference.Transform.Apply(Point3d.Origin);
        Assert.Equal(d, moved.X, 1e-6);
        Assert.Equal(d, moved.Y, 1e-6);
    }

    [Fact]
    public void The_pointer_snaps_to_the_edges_and_corners_of_meshes()
    {
        var floor = new MeshEntity([new(0, 0, 0), new(1000, 0, 0), new(1000, 1000, 0), new(0, 1000, 0)],
            [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]);
        _document.Root.Entities.Add(floor);

        _tool.Hover(On(floor, new Point3d(996, 300, 0)));
        Assert.Equal(new Inference(InferenceKind.OnEdge, new Point3d(1000, 300, 0), null, Vector3d.UnitY,
            Inference.Segment(new Point3d(1000, 0, 0), new Point3d(1000, 1000, 0))), _host.Preview.Inference);

        _tool.Hover(On(floor, new Point3d(997, 996, 0)));
        Inference corner = _host.Preview.Inference!;
        Assert.Equal((InferenceKind.Endpoint, new Point3d(1000, 1000, 0)), (corner.Kind, corner.Point));
        // Both edges it is an end of are shown, whole.
        Assert.Equal(2, corner.Edges!.Count);
        Assert.All(corner.Edges, edge => Assert.Contains(new Point3d(1000, 1000, 0), edge));
        Assert.All(corner.Edges, edge => Assert.Equal(1000, edge[0].DistanceTo(edge[^1]), 6));
    }

    [Fact]
    public void Three_lengths_typed_move_by_them_along_the_axes()
    {
        var block = new Block("Part");
        block.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)));
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        _document.Root.Entities.Add(reference);
        _host.SelectedPath = [reference];

        _tool.Click(Ground(0, 0));
        Assert.True(_tool.TypeMeasurement("500, 0, 120"));

        Assert.Equal(new Point3d(500, 0, 120), reference.Transform.Apply(Point3d.Origin));
        Assert.False(_tool.TypeMeasurement("500, 0"));
    }

    [Fact]
    public void Clicked_at_the_middle_of_a_face_of_its_box_a_selection_moves_freely_grabbed_by_a_handle_along_an_axis_only()
    {
        var block = new Block("Part");
        block.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100)));
        _document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        _document.Root.Entities.Add(reference);
        _host.SelectedPath = [reference];
        var top = new Inference(InferenceKind.Centre, new Point3d(50, 50, 100), new Plane(Vector3d.UnitZ, 100), Vector3d.UnitZ);

        // From the tool, freely: to a point of the ground aside.
        Assert.True(_tool.Grab(top));
        _tool.Click(Ground(550, 50));
        Assert.Equal(new Point3d(500, 0, 0), reference.Transform.Apply(Point3d.Origin));
        _host.Session.Undo();

        // From a handle of the select tool, along the axis it goes most along: up, the pointer above the top.
        Assert.True(_tool.Grab(top, constrained: true));
        _tool.Click(new ToolInput(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(800, 50, 300), new Vector3d(-1, 0, 0), 1, 0))));
        Assert.Equal(new Point3d(0, 0, 200), reference.Transform.Apply(Point3d.Origin));
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
