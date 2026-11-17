using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class RotateToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly RotateTool _tool;

    public RotateToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool = new RotateTool(_kernel);
        _tool.Activate(_host);
    }

    private static ToolInput Ground(double x, double y, ToolModifiers modifiers = ToolModifiers.None) =>
        new(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)), modifiers);

    [Fact]
    public void A_rotation_turns_points_counterclockwise_around_its_axis()
    {
        Transform quarter = Transform.Rotation(new Point3d(100, 0, 0), Vector3d.UnitZ, Math.PI / 2);
        Point3d turned = quarter.Apply(new Point3d(200, 0, 5));
        Assert.Equal(100, turned.X, precision: 9);
        Assert.Equal(100, turned.Y, precision: 9);
        Assert.Equal(5, turned.Z, precision: 9);
        Assert.Throws<ArgumentException>(() => Transform.Rotation(default, default, 1));
    }

    [Fact]
    public void The_selection_turns_by_the_angle_clicked_snapped_to_steps_of_fifteen_degrees()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(200, 100, 50));
        _document.Root.Entities.Add(box);
        _host.SelectedPath = [box];
        Assert.Equal("Angle", _tool.MeasurementName);

        _tool.Click(Ground(-1000, -1000)); // the centre
        _tool.Click(Ground(0, -1000)); // along X
        _tool.Hover(Ground(-1000 + 1000 * Math.Cos(Math.PI * 91 / 180), -1000 + 1000 * Math.Sin(Math.PI * 91 / 180)));
        Assert.Equal("90°", _tool.Measurement);
        _tool.Click(Ground(-1000, 0));

        BoundingBox moved = box.GetBoundingBox();
        Assert.Equal(-2100, moved.Min.X, precision: 6);
        Assert.Equal(0, moved.Min.Y, precision: 6);
        Assert.Equal(-2000, moved.Max.X, precision: 6);
        Assert.Equal(200, moved.Max.Y, precision: 6);
    }

    [Fact]
    public void With_Option_a_copy_turns_by_the_angle_typed_towards_the_pointer()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(200, 100, 50));
        _document.Root.Entities.Add(box);
        _host.SelectedPath = [box];

        _tool.Click(Ground(-1000, -1000));
        _tool.Click(Ground(0, -1000));
        _tool.Hover(Ground(-500, -1500, ToolModifiers.Alt)); // clockwise
        Assert.True(_tool.TypeMeasurement("90"));

        Assert.Equal(2, _document.Root.Entities.Count);
        Assert.Equal(0, box.GetBoundingBox().Min.X, precision: 6);
        BoundingBox copy = _document.Root.Entities[1].GetBoundingBox();
        Assert.Equal(100, copy.Max.X, precision: 6);
        Assert.Equal(-2200, copy.Min.Y, precision: 6);
        Assert.Equal(-2000, copy.Max.Y, precision: 6);
    }

    [Theory]
    [InlineData(85, 90)]
    [InlineData(-94, -90)]
    [InlineData(176, 180)]
    [InlineData(46, 45)]
    [InlineData(80, 80)]
    public void Angles_snap_to_right_angles_and_closely_to_steps(double degrees, double snapped) =>
        Assert.Equal(snapped, RotateTool.Snap(degrees));
}
