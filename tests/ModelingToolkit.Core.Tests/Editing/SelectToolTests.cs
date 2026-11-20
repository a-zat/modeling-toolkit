using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class SelectToolTests
{
    private readonly Document _document = new();
    private readonly BrepEntity _shape = new(new NoKernel(), new NoShape());
    private readonly BlockReference _left, _right;
    private readonly BrepEntity _loose = new(new NoKernel(), new NoShape());
    private readonly Host _host;
    private readonly SelectTool _tool = new(new NoKernel());

    public SelectToolTests()
    {
        var part = new Block("Part") { Entities = { _shape } };
        _document.Blocks.Add(part);
        _left = new BlockReference(part, Transform.Identity) { Name = "Left" };
        _right = new BlockReference(part, Transform.Translation(50, 0, 0));
        _document.Root.Entities.Add(_left);
        _document.Root.Entities.Add(_right);
        _document.Root.Entities.Add(_loose);
        _host = new Host(_document);
        _tool.Activate(_host);
    }

    private static ToolInput On(ElementReference element, ToolModifiers modifiers = ToolModifiers.None, int clicks = 1) =>
        new(new ToolPick(element.Path, element, default, default), modifiers, clicks);

    private ElementReference Face(BlockReference reference) => new([reference, _shape], ElementKind.Face, 0);

    [Fact]
    public void A_click_on_a_shape_of_the_block_selects_its_element_and_shift_adds_or_removes()
    {
        ElementReference first = new([_loose], ElementKind.Edge, 0), second = new([_loose], ElementKind.Edge, 1);
        _host.SelectEntity([_left]);

        _tool.Click(On(first));
        _tool.Click(On(second, ToolModifiers.Shift));
        _tool.Click(On(first, ToolModifiers.Shift));

        Assert.Equal([second], _host.Elements.Elements);
        Assert.Null(_host.SelectedEntity);
    }

    [Fact]
    public void A_shift_click_selects_the_innermost_component_under_the_pointer_entering_the_blocks_around_it()
    {
        // A room with a table with a chair, the chair placing the part.
        var table = new Block("Table");
        var chair = new BlockReference(_left.Block, Transform.Identity) { Name = "Chair" };
        table.Entities.Add(chair);
        var room = new Block("Room");
        var tableReference = new BlockReference(table, Transform.Identity);
        room.Entities.Add(tableReference);
        _document.Blocks.Add(table);
        _document.Blocks.Add(room);
        var roomReference = new BlockReference(room, Transform.Identity);
        _document.Root.Entities.Add(roomReference);
        ElementReference face = new([roomReference, tableReference, chair, _shape], ElementKind.Face, 0);

        _tool.Click(On(face, ToolModifiers.Shift));

        Assert.Equal([roomReference, tableReference], _host.Context.Path);
        Assert.Equal([roomReference, tableReference, chair], _host.SelectedEntity);
        _host.Context.Leave();
        Assert.Equal([roomReference], _host.Context.Path);
    }

    [Fact]
    public void A_click_selects_a_reference_and_a_double_click_enters_it()
    {
        _tool.Click(On(Face(_left)));
        Assert.Equal([_left], _host.SelectedEntity);

        _tool.Click(On(Face(_left), clicks: 2));
        Assert.Equal([_left], _host.Context.Path);
        Assert.Null(_host.SelectedEntity);

        _tool.Click(On(Face(_left)));
        Assert.Equal([Face(_left)], _host.Elements.Elements);
    }

    [Fact]
    public void Only_a_double_click_outside_the_block_leaves_it()
    {
        _host.Context.Enter(_left);

        _tool.Click(On(Face(_right)));
        Assert.False(_host.Context.IsModel); // a single click outside does nothing
        Assert.Null(_host.SelectedEntity);

        _tool.Click(On(Face(_right), clicks: 2));
        Assert.True(_host.Context.IsModel);
        Assert.Equal([_right], _host.SelectedEntity);

        _host.Context.Enter(_right);
        _tool.Click(new ToolInput(ToolPick.Nothing));
        Assert.False(_host.Context.IsModel);
        _tool.Click(new ToolInput(ToolPick.Nothing, ClickCount: 2));
        Assert.True(_host.Context.IsModel);
        Assert.Null(_host.SelectedEntity);
    }

    [Fact]
    public void Escape_clears_the_selection_and_then_leaves_the_block()
    {
        _host.Context.Enter(_left);
        _tool.Click(On(Face(_left)));

        Assert.True(_tool.Key(ToolKey.Escape, ToolModifiers.None));
        Assert.Empty(_host.Elements.Elements);
        Assert.False(_host.Context.IsModel);
        Assert.True(_tool.Key(ToolKey.Escape, ToolModifiers.None));
        Assert.True(_host.Context.IsModel);
        Assert.False(_tool.Key(ToolKey.Escape, ToolModifiers.None));
        Assert.False(_tool.Key(ToolKey.Enter, ToolModifiers.None));
    }

    [Fact]
    public void The_hint_tells_what_a_click_would_do_on_what_is_under_the_pointer()
    {
        Assert.Equal("Click an object to select it", _tool.Hint);
        var hints = new List<string>();
        _tool.HintChanged += (_, _) => hints.Add(_tool.Hint);

        _tool.Hover(On(Face(_left)));
        _tool.Hover(On(Face(_left)));
        _tool.Hover(On(new ElementReference([_loose], ElementKind.Vertex, 0)));
        _host.Context.Enter(_left);
        _tool.Hover(On(Face(_right)));
        _tool.Hover(new ToolInput(ToolPick.Nothing));

        Assert.Equal(
        [
            "Click to select Left, double-click to edit its block",
            "Click to select the vertex, Shift-click to add or remove it",
            "Double click to leave the block and select there",
            "Double click outside the block or press Esc to leave it",
        ], hints);
    }

    [Fact]
    public void An_inactive_tool_has_no_host()
    {
        _tool.Deactivate();

        Assert.False(_tool.IsActive);
        Assert.Throws<InvalidOperationException>(() => _tool.Click(On(Face(_left))));
    }

    internal sealed class Host(Document document) : IToolHost
    {
        public EditSession Session { get; } = new(document);

        public EditContext Context { get; } = new(document);

        public ElementSelection Elements { get; } = new(document);

        public IReadOnlyList<Entity>? SelectedEntity { get; private set; }

        public void SelectEntity(IReadOnlyList<Entity>? path) => SelectedEntity = path;

        public void ShowPreview(ToolPreview preview)
        {
        }

        public List<string> Reports { get; } = [];

        public void Report(string message) => Reports.Add(message);
    }
}
