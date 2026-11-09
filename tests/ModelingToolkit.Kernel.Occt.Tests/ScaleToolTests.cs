using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class ScaleToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly ScaleTool _tool;
    private readonly BrepEntity _box;

    public ScaleToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool = new ScaleTool(_kernel);
        _tool.Activate(_host);
        _box = new BrepEntity(_kernel, _kernel.MakeBox(100, 100, 100));
        var red = new Material("Red");
        _box.Paint([0], red);
        _document.Root.Entities.Add(_box);
        _host.SelectedPath = [_box];
    }

    private static ToolInput Ground(double x, double y, ToolModifiers modifiers = ToolModifiers.None) =>
        new(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)), modifiers);

    [Fact]
    public void The_selection_scales_by_the_ratio_of_the_distances_from_the_point_that_stays()
    {
        Assert.Equal("Scale", _tool.MeasurementName);
        _tool.Click(Ground(1000, 1000)); // stays
        _tool.Click(Ground(1100, 1000)); // reference
        _tool.Hover(Ground(1250, 1040));
        Assert.Equal("2.5", _tool.Measurement);
        _tool.Click(Ground(1250, 1040));

        BoundingBox box = _box.GetBoundingBox();
        Assert.Equal(-1500, box.Min.X, precision: 6);
        Assert.Equal(-1250, box.Max.X, precision: 6);
        Assert.Equal(250, box.Max.Z, precision: 6);
        Assert.Equal(250 * 250 * 250, _kernel.ComputeVolume(_box.Shape), precision: 3);
        Assert.NotNull(_box.MaterialOf(0));
        _host.Session.Undo();
        Assert.Equal(1e6, _kernel.ComputeVolume(_box.Shape), precision: 3);
    }

    [Fact]
    public void A_factor_or_a_length_can_be_typed_and_Option_scales_a_copy()
    {
        _tool.Click(Ground(0, 0));
        Assert.True(_tool.TypeMeasurement("0.5"));
        Assert.Equal(125000, _kernel.ComputeVolume(_box.Shape), precision: 3);

        _tool.Click(Ground(0, 0));
        _tool.Click(Ground(200, 0));
        _tool.Hover(Ground(300, 0, ToolModifiers.Alt));
        Assert.False(_tool.TypeMeasurement("-2"));
        Assert.True(_tool.TypeMeasurement("400mm"));

        Assert.Equal(2, _document.Root.Entities.Count);
        Assert.Equal(1e6, _kernel.ComputeVolume(((BrepEntity)_document.Root.Entities[1]).Shape), precision: 3);
    }
}
