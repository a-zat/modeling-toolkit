using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class TapeToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly TapeTool _tool;

    public TapeToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool = new TapeTool(_kernel);
        _tool.Activate(_host);
    }

    private static ToolInput Ground(double x, double y) => new(new ToolPick(null, null, default, default,
        new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));

    [Fact]
    public void Two_points_are_measured()
    {
        _tool.Click(Ground(1000, 1000));
        _tool.Hover(Ground(1300, 1400));
        Assert.Equal("500 mm", _tool.Measurement);
        _tool.Click(Ground(1300, 1400));

        Assert.Equal("Length: 500 mm", Assert.Single(_host.Reports));
        Assert.Empty(_document.Root.Entities);
    }

    [Fact]
    public void From_an_edge_a_guide_is_put_parallel_to_it_where_clicked_or_at_a_typed_distance()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(1000, 1000, 1000));
        _document.Root.Entities.Add(box);
        IReadOnlyList<Entity> path = [box];
        // The edge along X on the ground, at y = 0.
        int edge = Enumerable.Range(0, 12).First(e => _kernel.GetEdgeInfo(box.Shape, e) is
            { Start.Z: 0, End.Z: 0, Start.Y: 0, End.Y: 0 });
        ToolInput onEdge = new(new ToolPick(path, new ElementReference(path, ElementKind.Edge, edge), new Point3d(500, 0, 0),
            default, new PointerRay(new Point3d(500, 0, 1000), new Vector3d(0, 0, -1), 1, 0)));

        _tool.Hover(onEdge);
        _tool.Click(onEdge);
        _tool.Hover(Ground(300, -400));
        Assert.Equal("400 mm", _tool.Measurement);
        _tool.Click(Ground(300, -400));

        var guide = Assert.IsType<GuideEntity>(_document.Root.Entities[1]);
        Assert.Equal(-400, guide.Point.Y, precision: 9);
        Assert.Equal(1, Math.Abs(guide.Direction.X), precision: 9);

        _tool.Hover(onEdge);
        _tool.Click(onEdge);
        _tool.Hover(Ground(300, -100));
        Assert.True(_tool.TypeMeasurement("250"));
        Assert.Equal(-250, ((GuideEntity)_document.Root.Entities[2]).Point.Y, precision: 9);

        _tool.Click(Ground(700, -403)); // snaps to the first guide
        _tool.Click(Ground(700, -250)); // and the second
        Assert.Equal("Length: 150 mm", _host.Reports[^1]);
    }
}
