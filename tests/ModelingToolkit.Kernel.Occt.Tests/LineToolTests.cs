using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class LineToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly Host _host;
    private readonly LineTool _tool;

    public LineToolTests()
    {
        _host = new Host(_document);
        _tool = new LineTool(_kernel);
        _tool.Activate(_host);
    }

    // The pointer over the ground at a point, looking down, one unit of the screen being 1 mm.
    private static ToolInput At(double x, double y) => new(new ToolPick(null, null, default, default,
        new PointerRay(new Point3d(x, y, 1000), new Vector3d(0, 0, -1), 1, 0)));

    private BrepEntity Geometry => Assert.Single(_document.Root.Entities.OfType<BrepEntity>());

    [Fact]
    public void Clicks_draw_segments_one_after_the_other_until_they_close_a_face()
    {
        _tool.Click(At(100, 100));
        Assert.Equal(new Point3d(100, 100, 0), _tool.Start);
        Assert.Equal("0 mm", _tool.Measurement);

        _tool.Hover(At(600, 103)); // snaps on the red axis
        Assert.Equal("500 mm", _tool.Measurement);
        Assert.Equal(InferenceKind.AxisX, _host.Preview.Inference?.Kind);
        Assert.Equal(new PreviewLine(new(100, 100, 0), new(600, 100, 0), InferenceKind.AxisX), Assert.Single(_host.Preview.Lines));

        _tool.Click(At(600, 103));
        Assert.Equal(new ShapeTopology(0, 0, 1, 2), _kernel.CountElements(Geometry.Shape));
        Assert.Equal(new Point3d(600, 100, 0), _tool.Start);

        _tool.Click(At(600, 400));
        _tool.Click(At(100, 400));
        // The first point, picked as the vertex it is now, as the view picks vertices near the pointer.
        IReadOnlyList<Entity> path = [Geometry];
        int first = Enumerable.Range(0, 4).Single(v => _kernel.GetVertexPoint(Geometry.Shape, v) == new Point3d(100, 100, 0));
        _tool.Click(At(100.4, 100.3) with
        {
            Pick = At(100.4, 100.3).Pick with { Path = path, Element = new ElementReference(path, ElementKind.Vertex, first) },
        });

        Assert.Equal(new ShapeTopology(0, 1, 4, 4), _kernel.CountElements(Geometry.Shape));
        Assert.Null(_tool.Start); // the face closed the line
        Assert.Null(_tool.Measurement);
        Assert.Equal(4, _host.Session.UndoCount()); // one step per segment
        _host.Session.Undo();
        Assert.Equal(0, _kernel.CountElements(Geometry.Shape).Faces);
    }

    [Fact]
    public void A_typed_length_ends_the_segment_towards_the_pointer()
    {
        _tool.Click(At(0, 500));
        _tool.Hover(At(0, 900)); // on the green axis

        Assert.True(_tool.TypeMeasurement("1.2 m"));

        Assert.Equal(new Point3d(0, 1700, 0), _tool.Start);
        Assert.Equal(1200, _kernel.GetEdgeInfo(Geometry.Shape, 0).Length, precision: 9);
        Assert.False(_tool.TypeMeasurement("long"));
    }

    [Fact]
    public void An_arrow_locks_the_axis_and_Escape_stops_the_line()
    {
        _tool.Click(At(100, 100));
        Assert.True(_tool.Key(ToolKey.Up, ToolModifiers.None));

        // Locked on Z, looking from the side: the point on the vertical line closest to the pointer.
        _tool.Hover(new ToolInput(new ToolPick(null, null, default, default,
            new PointerRay(new Point3d(100, -1000, 300), new Vector3d(0, 1, 0), 1, 0))));
        Assert.Equal(InferenceKind.AxisZ, _host.Preview.Inference?.Kind);
        Assert.Equal(300, _host.Preview.Inference!.Point.Z, precision: 9);

        Assert.True(_tool.Key(ToolKey.Escape, ToolModifiers.None));
        Assert.Null(_tool.Start);
        Assert.False(_tool.Key(ToolKey.Escape, ToolModifiers.None)); // nothing more to stop
        Assert.Empty(_document.Root.Entities);
    }

    [Fact]
    public void Lines_go_into_the_block_being_edited_in_its_coordinates()
    {
        var block = new Block("Part");
        var reference = new BlockReference(block, Transform.Translation(1000, 0, 0));
        _document.Blocks.Add(block);
        _document.Root.Entities.Add(reference);
        _host.Context.Enter(reference);

        _tool.Click(At(1000, 0)); // the origin of the block
        _tool.Click(At(1300, 2));

        BrepEntity line = Assert.Single(block.Entities.OfType<BrepEntity>());
        EdgeInfo edge = _kernel.GetEdgeInfo(line.Shape, 0);
        Assert.Equal(new Point3d(0, 0, 0), edge.Start);
        Assert.Equal(new Point3d(300, 0, 0), edge.End);
    }

    internal sealed class Host(Document document) : IToolHost
    {
        public EditSession Session { get; } = new(document);

        public EditContext Context { get; } = new(document);

        public ElementSelection Elements { get; } = new(document);

        public IReadOnlyList<Entity>? SelectedPath { get; set; }

        public IReadOnlyList<Entity>? SelectedEntity => SelectedPath;

        public ToolPreview Preview { get; private set; } = ToolPreview.Empty;

        public List<string> Reports { get; } = [];

        public void SelectEntity(IReadOnlyList<Entity>? path)
        {
        }

        public void ShowPreview(ToolPreview preview) => Preview = preview;

        public void Report(string message) => Reports.Add(message);
    }
}

internal static class SessionExtensions
{
    public static int UndoCount(this EditSession session)
    {
        int count = 0;
        while (session.CanUndo)
        {
            session.Undo();
            count++;
        }
        for (int i = 0; i < count; i++)
        {
            session.Redo();
        }
        return count;
    }
}
