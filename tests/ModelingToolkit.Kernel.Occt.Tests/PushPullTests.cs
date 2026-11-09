using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class PushPullTests
{
    private readonly OcctKernel _kernel = new();

    private int FaceOn(IKernelShape shape, Vector3d normal, double offset, double? area = null) =>
        Enumerable.Range(0, _kernel.CountElements(shape).Faces).Single(face =>
            _kernel.GetFaceInfo(shape, face) is { Plane: { } plane } info &&
            (plane.Normal - normal).Length < 1e-9 && Math.Abs(plane.Offset - offset) < 1e-6 &&
            (area is null || Math.Abs(info.Area - area.Value) < 1e-6));

    [Fact]
    public void Pulling_the_top_of_a_box_makes_it_taller_and_pushing_it_lower()
    {
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);
        int top = FaceOn(box, Vector3d.UnitZ, 100);

        ShapeEdit taller = _kernel.PushPull(box, top, 50);
        using (taller.Shape)
        {
            Assert.Equal(new ShapeTopology(1, 6, 12, 8), _kernel.CountElements(taller.Shape));
            Assert.Equal(1.5e6, _kernel.ComputeVolume(taller.Shape), precision: 3);
            Assert.Equal(top, taller.FaceOrigins[FaceOn(taller.Shape, Vector3d.UnitZ, 150)]);
        }
        ShapeEdit lower = _kernel.PushPull(box, top, -30);
        using (lower.Shape)
        {
            Assert.Equal(0.7e6, _kernel.ComputeVolume(lower.Shape), precision: 3);
        }
        Assert.Equal(4, _kernel.GetFaceEdges(box, top).Count);
        Assert.Throws<ArgumentException>(() => _kernel.PushPull(box, top, 0));
        Assert.Throws<GeometryKernelException>(() => _kernel.PushPull(box, top, -100));
    }

    [Fact]
    public void The_faces_pulled_from_a_painted_face_are_painted_until_undone()
    {
        var document = new Document();
        LineCurve[] square =
        [
            new(new(20, 20, 100), new(80, 20, 100)), new(new(80, 20, 100), new(80, 80, 100)),
            new(new(80, 80, 100), new(20, 80, 100)), new(new(20, 80, 100), new(20, 20, 100)),
        ];
        using IKernelShape box = _kernel.MakeBox(100, 100, 100);
        var entity = new BrepEntity(_kernel, _kernel.AddEdges(box, square).Shape);
        document.Root.Entities.Add(entity);
        int inner = FaceOn(entity.Shape, Vector3d.UnitZ, 100, 3600);
        var red = new Material("Red");
        entity.Paint([inner], red);
        var session = new EditSession(document);

        session.Execute(new ReplaceShapeCommand("Push/Pull", entity, _kernel.PushPull(entity.Shape, inner, 40)));

        Assert.Equal(11, _kernel.CountElements(entity.Shape).Faces);
        Assert.Equal(5, entity.FaceMaterials.Values.Count(material => ReferenceEquals(material, red)));
        Assert.Same(red, entity.MaterialOf(FaceOn(entity.Shape, Vector3d.UnitZ, 140)));
        session.Undo();
        Assert.Equal(7, _kernel.CountElements(entity.Shape).Faces);
        Assert.Equal([inner], entity.FaceMaterials.Keys);
    }

    [Fact]
    public void The_tool_pulls_a_face_to_the_pointer_or_by_a_typed_distance()
    {
        var document = new Document();
        var host = new LineToolTests.Host(document);
        var entity = new BrepEntity(_kernel, _kernel.MakeBox(1000, 1000, 1000));
        document.Root.Entities.Add(entity);
        IReadOnlyList<Entity> path = [entity];
        var tool = new PushPullTool(_kernel);
        tool.Activate(host);
        Assert.Equal("Distance", tool.MeasurementName);

        ToolInput OnTop() => new(new ToolPick(path,
            new ElementReference(path, ElementKind.Face, FaceOn(entity.Shape, Vector3d.UnitZ, Top(entity.Shape))),
            new Point3d(500, 500, Top(entity.Shape)), Vector3d.UnitZ,
            new PointerRay(new Point3d(500, 500, 5000), new Vector3d(0, 0, -1), 1, 0)));
        // From the side, level with a height: the nearest point of the vertical line through the point clicked.
        static ToolInput Level(double z) => new(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(500, -5000, z), new Vector3d(0, 1, 0), 1, 0)));

        tool.Hover(OnTop());
        Assert.Equal(4, host.Preview.Lines.Count);
        tool.Click(OnTop());
        tool.Hover(Level(1300));
        Assert.Equal("300 mm", tool.Measurement);
        Assert.Equal(12, host.Preview.Lines.Count); // the outline, where it goes, and the lines between
        tool.Click(Level(1300));
        Assert.Equal(1.3e9, _kernel.ComputeVolume(entity.Shape), precision: 0);

        tool.Click(OnTop());
        tool.Hover(Level(1100));
        Assert.True(tool.TypeMeasurement("100"));
        Assert.Equal(1.2e9, _kernel.ComputeVolume(entity.Shape), precision: 0);
        Assert.Equal(2, host.Session.UndoCount());
        Assert.False(tool.TypeMeasurement("100"));
    }

    private double Top(IKernelShape shape) => _kernel.GetBoundingBox(shape).Max.Z;

    [Fact]
    public void The_tool_makes_a_solid_of_a_rectangle_drawn_on_the_ground()
    {
        var document = new Document();
        var host = new LineToolTests.Host(document);
        var rectangle = new RectangleTool(_kernel);
        rectangle.Activate(host);
        ToolInput Ground(double x, double y) => new(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));
        rectangle.Click(Ground(0, 0));
        rectangle.Click(Ground(400, 300));
        BrepEntity entity = Assert.Single(document.Root.Entities.OfType<BrepEntity>());
        IReadOnlyList<Entity> path = [entity];
        Vector3d normal = _kernel.GetFaceInfo(entity.Shape, 0).Plane!.Value.Normal;

        var tool = new PushPullTool(_kernel);
        tool.Activate(host);
        tool.Click(new ToolInput(new ToolPick(path, new ElementReference(path, ElementKind.Face, 0),
            new Point3d(200, 150, 0), normal, new PointerRay(new Point3d(200, 150, 1000), new Vector3d(0, 0, -1), 1, 0))));
        tool.Hover(new ToolInput(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(200, -5000, 250), new Vector3d(0, 1, 0), 1, 0))));
        Assert.Equal("250 mm", tool.Measurement); // up, towards the viewer, whichever way the face turns
        Assert.True(tool.TypeMeasurement("250"));

        Assert.Equal(new ShapeTopology(1, 6, 12, 8), _kernel.CountElements(entity.Shape));
        Assert.Equal(400 * 300 * 250, _kernel.ComputeVolume(entity.Shape), precision: 3);
        Assert.Equal(250, _kernel.GetBoundingBox(entity.Shape).Max.Z, precision: 6);
    }
}
