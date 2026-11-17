using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class SelectionHandlesTests
{
    private const double Tolerance = 1e-6;

    private readonly Document _document = new();
    private readonly BlockReference _reference;

    public SelectionHandlesTests()
    {
        var block = new Block("Part");
        // A box 100 mm wide, deep and high, from the origin.
        block.Entities.Add(new MeshEntity(
            [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 100)], [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]));
        _document.Blocks.Add(block);
        _reference = new BlockReference(block, Transform.Translation(1000, 0, 0));
        _document.Root.Entities.Add(_reference);
    }

    // Looking straight down at a point, from 1 m above it.
    private static PointerRay Down(double x, double y, double z = 0) =>
        new(new Point3d(x, y, z + 1000), new Vector3d(0, 0, -1), 1, 0);

    [Fact]
    public void The_handles_are_at_the_middles_of_the_bottom_the_top_and_the_edges_of_the_bottom()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);

        Assert.Equal(new SelectionHandle(HandleKind.Move, new Point3d(1050, 50, 0)), handles.Handles[0]);
        Assert.Equal(new SelectionHandle(HandleKind.Lift, new Point3d(1050, 50, 100)), handles.Handles[1]);
        Assert.Equal(4, handles.Handles.Count(handle => handle.Kind == HandleKind.Rotate));
        Assert.Equal(12, handles.Box(Transform.Identity).Count());
    }

    [Fact]
    public void Dragging_the_bottom_moves_snapping_along_the_axes_and_the_top_lifts()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);

        HandleDrag move = handles.Drag(handles.Handles[0], Down(1550, 80))!;
        Assert.Equal(InferenceKind.AxisX, move.Along);
        Assert.Equal(new Point3d(500, 0, 0), move.Transform.Apply(Point3d.Origin));

        HandleDrag free = handles.Drag(handles.Handles[0], Down(1350, 350))!;
        Assert.Equal(InferenceKind.None, free.Along);

        var aside = new PointerRay(new Point3d(0, 50, 300), new Vector3d(1, 0, 0), 1, 0);
        HandleDrag lift = handles.Drag(handles.Handles[1], aside)!;
        Assert.Equal(200, lift.Transform.Apply(Point3d.Origin).Z, Tolerance);
    }

    [Fact]
    public void Dragging_an_edge_turns_around_the_bottom_snapping_to_right_angles()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);
        SelectionHandle front = handles.Handles.First(handle => handle.Kind == HandleKind.Rotate && handle.Point.Y < 1);

        // From the front, (1050, 0), around (1050, 50), to nearly the right side.
        HandleDrag turn = handles.Drag(front, Down(1100, 46))!;

        Assert.True(turn.IsAngle);
        Assert.Equal(90, turn.Measure, Tolerance);
        Point3d corner = turn.Transform.Apply(new Point3d(1000, 0, 0));
        Assert.Equal(1100, corner.X, Tolerance);
        Assert.Equal(0, corner.Y, Tolerance);
    }

    [Fact]
    public void The_select_tool_drags_a_handle_of_the_selection_as_one_command()
    {
        var host = new SelectToolTests.Host(_document);
        var tool = new SelectTool();
        tool.Activate(host);
        host.SelectEntity([_reference]);
        Assert.NotNull(tool.Handles);

        tool.Press(new ToolInput(ToolPick.Nothing, Handle: 0));
        Assert.True(tool.IsDraggingHandle);
        tool.Drag(new ToolInput(new ToolPick(null, null, default, default, Down(1050, 550)), Handle: 0));
        tool.Release(new ToolInput(ToolPick.Nothing, Handle: 0));

        Assert.False(tool.IsDraggingHandle);
        Assert.Equal(new Point3d(1000, 500, 0), _reference.Transform.Apply(Point3d.Origin));
        host.Session.Undo();
        Assert.Equal(new Point3d(1000, 0, 0), _reference.Transform.Apply(Point3d.Origin));
    }
}
