using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class InteractToolTests
{
    [Fact]
    public void A_click_works_what_can_be_worked_and_the_hint_tells_what_can()
    {
        var door = new BlockReference(new Block("Door"), Core.Geometry.Transform.Identity);
        var wall = new MeshEntity([], []);
        var worked = new List<IReadOnlyList<Entity>?>();
        var tool = new InteractTool(path => path?.Contains(door) == true, path =>
        {
            worked.Add(path);
            return path?.Contains(door) == true;
        });
        var host = new SelectToolTests.Host(new Document());
        tool.Activate(host);
        string idle = tool.Hint;

        tool.Hover(new ToolInput(new ToolPick([door], null, default, default)));
        Assert.NotEqual(idle, tool.Hint);
        tool.Click(new ToolInput(new ToolPick([door], null, default, default)));
        Assert.Empty(host.Reports);

        tool.Hover(new ToolInput(new ToolPick([wall], null, default, default)));
        Assert.Equal(idle, tool.Hint);
        tool.Click(new ToolInput(new ToolPick([wall], null, default, default)));
        Assert.Single(host.Reports);
        Assert.Equal(2, worked.Count);
    }
}
