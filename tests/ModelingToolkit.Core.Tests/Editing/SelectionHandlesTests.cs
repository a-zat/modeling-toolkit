using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class SelectionHandlesTests
{
    private const double Tolerance = 1e-6;
    private const int TopFace = 8 + 12 + 1; // the corners, the edges, then the faces: bottom, top

    private readonly Document _document = new();
    private readonly BlockReference _reference;
    private readonly SelectToolTests.Host _host;
    private readonly SelectTool _tool = new(new NoKernel());

    public SelectionHandlesTests()
    {
        var block = new Block("Part");
        // A box 100 mm wide, deep and high, from the origin.
        block.Entities.Add(new MeshEntity(
            [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 100)], [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]));
        _document.Blocks.Add(block);
        _reference = new BlockReference(block, Transform.Translation(1000, 0, 0));
        _document.Root.Entities.Add(_reference);
        _host = new SelectToolTests.Host(_document);
    }

    // Looking straight down at a point, from 1 m above it.
    private static PointerRay Down(double x, double y, double z = 0) =>
        new(new Point3d(x, y, z + 1000), new Vector3d(0, 0, -1), 1, 0);

    private static ToolInput At(PointerRay ray, int handle) => new(new ToolPick(null, null, default, default, ray), Handle: handle);

    private Point3d Origin => _reference.Transform.Apply(Point3d.Origin);

    [Fact]
    public void The_corners_move_the_middles_of_the_edges_slide_and_the_middles_of_the_faces_turn()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);

        Assert.Equal(8, handles.Handles.Count(handle => handle.Kind == HandleKind.Move));
        Assert.Equal(12, handles.Handles.Count(handle => handle.Kind == HandleKind.Slide));
        Assert.Equal(6, handles.Handles.Count(handle => handle.Kind == HandleKind.Rotate));
        Assert.Equal(new Point3d(1000, 0, 0), handles.Handles[0].Point);
        Assert.Equal(new Point3d(1050, 50, 100), handles.Handles[TopFace].Point);
        Assert.Equal(Vector3d.UnitZ, handles.Handles[TopFace].Grip.Direction);
        Assert.Equal(12, handles.Box(Transform.Identity).Count());
    }

    [Fact]
    public void A_face_turns_around_its_normal_from_where_the_pointer_first_went_snapping_to_right_angles()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);
        SelectionHandle top = handles.Handles[TopFace];
        Vector3d? from = null;

        Assert.Null(SelectionHandles.Turn(top, Down(1051, 50, 100), ref from)); // still at the middle
        Assert.Equal(0, SelectionHandles.Turn(top, Down(1100, 50, 100), ref from)!.Measure, Tolerance);
        HandleDrag turn = SelectionHandles.Turn(top, Down(1052, 100, 100), ref from)!;

        Assert.True(turn.IsAngle);
        Assert.Equal(90, turn.Measure, Tolerance);
        Point3d corner = turn.Transform.Apply(new Point3d(1000, 0, 0));
        Assert.Equal(1100, corner.X, Tolerance);
        Assert.Equal(0, corner.Y, Tolerance);
    }

    [Fact]
    public void A_corner_moves_the_selection_anywhere_as_one_command()
    {
        _tool.Activate(_host);
        _host.SelectEntity([_reference]);

        _tool.Press(At(Down(1000, 0), 0));
        Assert.True(_tool.IsDraggingHandle);
        _tool.Drag(At(Down(1500, 300), 0));
        Assert.Equal("Distance", _tool.MeasurementName);
        _tool.Release(At(Down(1500, 300), 0));

        Assert.False(_tool.IsDraggingHandle);
        Assert.Equal(new Point3d(1500, 300, 0), Origin);
        _host.Session.Undo();
        Assert.Equal(new Point3d(1000, 0, 0), Origin);
    }

    [Fact]
    public void The_middle_of_an_edge_moves_the_selection_along_an_axis_only()
    {
        _tool.Activate(_host);
        _host.SelectEntity([_reference]);
        int front = _tool.Handles!.Handles.ToList().FindIndex(handle =>
            handle.Kind == HandleKind.Slide && handle.Point == new Point3d(1050, 0, 0));

        _tool.Press(At(Down(1050, 0), front));
        _tool.Drag(At(Down(1300, 60), front));
        _tool.Release(At(Down(1300, 60), front));

        Assert.Equal(new Point3d(1250, 0, 0), Origin);
    }

    [Fact]
    public void A_face_turns_the_selection_as_one_command_and_the_handles_follow_it()
    {
        _tool.Activate(_host);
        _host.SelectEntity([_reference]);

        _tool.Press(At(Down(1050, 50, 100), TopFace));
        _tool.Drag(At(Down(1100, 50, 100), TopFace));
        _tool.Drag(At(Down(1052, 100, 100), TopFace));
        Assert.Equal("Angle", _tool.MeasurementName);
        _tool.Release(At(Down(1052, 100, 100), TopFace));

        Assert.Equal(1100, Origin.X, Tolerance);
        Assert.Equal(0, Origin.Y, Tolerance);
        // The box turned with it is the same box: its corners are where they were, the first now at the old second.
        Assert.Equal(1100, _tool.Handles!.Handles[0].Point.X, Tolerance);
        Assert.Equal(0, _tool.Handles!.Handles[0].Point.Y, Tolerance);
        _host.Session.Undo();
        Assert.Equal(new Point3d(1000, 0, 0), _tool.Handles!.Handles[0].Point);
    }

    [Fact]
    public void Escape_lets_a_handle_go_without_a_change()
    {
        _tool.Activate(_host);
        _host.SelectEntity([_reference]);

        _tool.Press(At(Down(1000, 0), 0));
        _tool.Drag(At(Down(1500, 300), 0));
        Assert.True(_tool.Key(ToolKey.Escape, ToolModifiers.None));
        _tool.Release(At(Down(1500, 300), 0));

        Assert.Equal(new Point3d(1000, 0, 0), Origin);
        Assert.False(_host.Session.CanUndo);
    }

    [Fact]
    public void The_points_of_the_move_tool_follow_the_selection_and_hide_while_it_moves()
    {
        var move = new MoveTool(new NoKernel());
        move.Activate(_host);
        _host.SelectEntity([_reference]);
        Assert.Contains(move.GripPoints, grip => grip.Point == new Point3d(1000, 0, 0));

        move.Click(new ToolInput(new ToolPick(null, null, default, default, Down(1000, 0))));
        Assert.Empty(move.GripPoints);
        move.Click(new ToolInput(new ToolPick(null, null, default, default, Down(1000, 700))));

        Assert.Equal(new Point3d(1000, 700, 0), Origin);
        Assert.Contains(move.GripPoints, grip => grip.Point == new Point3d(1000, 700, 0));
        Assert.DoesNotContain(move.GripPoints, grip => grip.Point == new Point3d(1000, 0, 0));
    }

    [Fact]
    public void Only_the_handles_on_the_faces_turned_to_the_eye_show_and_no_turn_on_a_face_seen_edge_on()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);

        // From the front, right and above: the faces at X = 1100, Y = 0 and the top.
        IReadOnlyList<int> seen = handles.VisibleFrom(new Point3d(4000, -3000, 3000));
        Assert.Equal(7, seen.Count(i => handles.Handles[i].Kind == HandleKind.Move));
        Assert.Equal(9, seen.Count(i => handles.Handles[i].Kind == HandleKind.Slide));
        Assert.Equal(3, seen.Count(i => handles.Handles[i].Kind == HandleKind.Rotate));
        Assert.DoesNotContain(seen, i => handles.Handles[i].Point == new Point3d(1000, 100, 0)); // the corner behind

        // Straight from the front, at the height of the middle: the top and the sides are seen edge-on.
        IReadOnlyList<int> front = handles.VisibleFrom(new Point3d(1050, -3000, 50));
        SelectionHandle turn = Assert.Single(front.Select(i => handles.Handles[i]), handle => handle.Kind == HandleKind.Rotate);
        Assert.Equal(-Vector3d.UnitY, turn.Grip.Direction);
    }

    [Fact]
    public void The_arrow_to_turn_lies_on_its_face()
    {
        var handles = new SelectionHandles(_reference, Transform.Identity);
        SelectionHandle top = handles.Handles[TopFace];

        IReadOnlyList<IReadOnlyList<Point3d>> arrow = handles.TurnArrow(top);

        Assert.Equal(2, arrow.Count);
        Assert.All(arrow.SelectMany(stroke => stroke), point => Assert.Equal(100, point.Z, Tolerance));
        Assert.All(arrow[0], point => Assert.Equal(20, point.DistanceTo(top.Point), Tolerance)); // 0.4 of half of 100
        Assert.Empty(handles.TurnArrow(handles.Handles[0]));
    }
}
