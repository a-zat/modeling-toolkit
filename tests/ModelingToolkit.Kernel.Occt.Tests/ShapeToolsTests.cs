using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class ShapeToolsTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;

    public ShapeToolsTests() => _host = new LineToolTests.Host(_document);

    private static ToolInput At(double x, double y) => new(new ToolPick(null, null, default, default,
        new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));

    private T Active<T>(T tool) where T : Tool
    {
        tool.Activate(_host);
        return tool;
    }

    private IKernelShape Shape => Assert.Single(_document.Root.Entities.OfType<BrepEntity>()).Shape;

    private double Area => Enumerable.Range(0, _kernel.CountElements(Shape).Faces).Sum(f => _kernel.GetFaceInfo(Shape, f).Area);

    [Fact]
    public void A_rectangle_is_drawn_by_two_corners_or_a_typed_size_towards_the_pointer()
    {
        RectangleTool tool = Active(new RectangleTool(_kernel));
        Assert.Equal("Size", tool.MeasurementName);

        tool.Click(At(100, 100));
        tool.Hover(At(400, 300));
        Assert.Equal("300 × 200 mm", tool.Measurement);
        Assert.Equal(4, _host.Preview.Lines.Count);
        tool.Click(At(400, 300));

        Assert.Equal(new ShapeTopology(0, 1, 4, 4), _kernel.CountElements(Shape));
        Assert.Equal(60000, Area, precision: 6);

        // Typed, towards the pointer: down and to the left of the corner.
        tool.Click(At(1000, 1000));
        tool.Hover(At(900, 950));
        Assert.True(tool.TypeMeasurement("500, 250"));
        Assert.Equal(60000 + 125000, Area, precision: 6);
        Assert.Contains(new Point3d(500, 750, 0),
            Enumerable.Range(0, _kernel.CountElements(Shape).Vertices).Select(v => _kernel.GetVertexPoint(Shape, v)));
        Assert.False(tool.TypeMeasurement("500"));
    }

    [Fact]
    public void A_rectangle_on_a_face_splits_it()
    {
        _document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(1000, 1000, 1000)));
        BrepEntity box = Assert.Single(_document.Root.Entities.OfType<BrepEntity>());
        IReadOnlyList<Entity> path = [box];
        int top = Enumerable.Range(0, 6).First(f => _kernel.GetFaceInfo(box.Shape, f).Plane is { Normal.Z: > 0.5 });
        ToolInput OnTop(double x, double y) => new(new ToolPick(path, new ElementReference(path, ElementKind.Face, top),
            new Point3d(x, y, 1000), new Vector3d(0, 0, 1), new PointerRay(new Point3d(x, y, 2000), new Vector3d(0, 0, -1), 1, 0)));
        RectangleTool tool = Active(new RectangleTool(_kernel));

        tool.Click(OnTop(200, 200));
        tool.Click(OnTop(600, 500));

        Assert.Equal(new ShapeTopology(1, 7, 16, 12), _kernel.CountElements(box.Shape));
    }

    [Fact]
    public void A_circle_makes_a_disc_of_the_radius_clicked_or_typed()
    {
        CircleTool tool = Active(new CircleTool(_kernel));
        Assert.Equal("Radius", tool.MeasurementName);

        tool.Click(At(0, 500));
        tool.Hover(At(300, 500));
        Assert.Equal("300 mm", tool.Measurement);
        Assert.True(_host.Preview.Lines.Count > 40);
        Assert.True(tool.TypeMeasurement("250"));

        Assert.Equal(Math.PI * 250 * 250, Area, precision: 3);
        Assert.Equal(CurveKind.Circle, _kernel.GetEdgeInfo(Shape, 0).Kind);
    }

    [Fact]
    public void An_arc_goes_through_its_ends_and_bulges_as_clicked_or_typed()
    {
        ArcTool tool = Active(new ArcTool(_kernel));
        tool.Click(At(0, 0));
        tool.Hover(At(600, 3));
        Assert.Equal("600 mm", tool.Measurement); // snapped on the red axis
        tool.Click(At(600, 3));
        Assert.Equal("Bulge", tool.MeasurementName);

        tool.Hover(At(300, 200));
        Assert.Equal("200 mm", tool.Measurement);
        // The preview passes through the top of the bulge, halfway along.
        PreviewLine half = _host.Preview.Lines[1 + 23];
        Assert.Equal(300, half.End.X, precision: 6);
        Assert.Equal(200, half.End.Y, precision: 6);

        Assert.True(tool.TypeMeasurement("300")); // a half circle, on the side of the pointer
        EdgeInfo arc = _kernel.GetEdgeInfo(Shape, 0);
        Assert.Equal(CurveKind.Arc, arc.Kind);
        Assert.Equal(300, Assert.NotNull(arc.Circle).Radius, precision: 6);
        Assert.Equal(Math.PI * 300, arc.Length, precision: 6);
        Assert.Equal(300, arc.Middle.Y, precision: 6);
    }
}
