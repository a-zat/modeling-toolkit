using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class OffsetToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly OffsetTool _tool;
    private readonly BrepEntity _square;

    public OffsetToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool = new OffsetTool(_kernel);
        _tool.Activate(_host);
        Point3d a = new(0, 0, 0), b = new(1000, 0, 0), c = new(1000, 1000, 0), d = new(0, 1000, 0);
        _square = new BrepEntity(_kernel, _kernel.AddEdges(null, [new LineCurve(a, b), new LineCurve(b, c), new LineCurve(c, d), new LineCurve(d, a)]).Shape);
        _document.Root.Entities.Add(_square);
    }

    private ToolInput OnFace(double x, double y) => new(new ToolPick([_square],
        new ElementReference([_square], ElementKind.Face, 0), new Point3d(x, y, 0), Vector3d.UnitZ,
        new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));

    private static ToolInput Over(double x, double y) => new(new ToolPick(null, null, default, default,
        new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));

    private double Area => Enumerable.Range(0, _kernel.CountElements(_square.Shape).Faces)
        .Sum(face => _kernel.GetFaceInfo(_square.Shape, face).Area);

    [Fact]
    public void Inside_the_face_the_outline_is_offset_inwards_by_the_distance_to_the_pointer()
    {
        _tool.Hover(OnFace(500, 500));
        Assert.Equal(4, _host.Preview.Lines.Count);
        _tool.Click(OnFace(500, 500));
        _tool.Hover(OnFace(300, 450));
        Assert.Equal("300 mm", _tool.Measurement);
        _tool.Click(OnFace(300, 450));

        Assert.Equal(new ShapeTopology(0, 2, 8, 8), _kernel.CountElements(_square.Shape));
        Assert.Equal(1e6, Area, precision: 3);
        Assert.Contains(Enumerable.Range(0, 8).Select(v => _kernel.GetVertexPoint(_square.Shape, v)),
            point => point.DistanceTo(new Point3d(300, 300, 0)) < 1e-6);
    }

    [Fact]
    public void Outside_a_typed_distance_adds_a_ring_around_the_face()
    {
        _tool.Click(OnFace(500, 500));
        _tool.Hover(Over(1300, 500));
        Assert.Equal("300 mm", _tool.Measurement);
        Assert.True(_tool.TypeMeasurement("100"));

        Assert.Equal(2, _kernel.CountElements(_square.Shape).Faces);
        Assert.Equal(1200 * 1200, Area, precision: 3);
        Assert.False(_tool.TypeMeasurement("100"));
    }
}
