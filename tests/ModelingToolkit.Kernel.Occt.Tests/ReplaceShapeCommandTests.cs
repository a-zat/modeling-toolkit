using System.Text;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class ReplaceShapeCommandTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void A_valid_result_replaces_the_shape_and_undoes_to_the_previous_one()
    {
        var document = new Document();
        var entity = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        document.Root.Entities.Add(entity);
        IKernelShape before = entity.Shape;
        IKernelShape drilled = _kernel.Subtract(before, _kernel.MakeCylinder(2, 10));
        var session = new EditSession(document);

        session.Execute(new ReplaceShapeCommand("Drill", entity, drilled));
        Assert.Same(drilled, entity.Shape);
        Assert.Equal("Drill", session.UndoName);

        session.Undo();
        Assert.Same(before, entity.Shape);
        session.Redo();
        Assert.Same(drilled, entity.Shape);
    }

    [Fact]
    public void An_invalid_result_is_refused_and_the_shape_stays()
    {
        var document = new Document();
        var entity = new BrepEntity(_kernel, _kernel.MakeBox(10, 20, 30));
        document.Root.Entities.Add(entity);
        IKernelShape before = entity.Shape;
        var session = new EditSession(document);

        var exception = Assert.Throws<EditRefusedException>(
            () => session.Execute(new ReplaceShapeCommand("Move Corner", entity, Invalid(before))));

        Assert.Contains("not valid", exception.Message, StringComparison.Ordinal);
        Assert.Same(before, entity.Shape);
        Assert.False(session.CanUndo);
    }

    // The shape with a corner moved away from the edges that end there.
    private IKernelShape Invalid(IKernelShape shape)
    {
        string text = Encoding.ASCII.GetString(_kernel.Serialize(shape));
        int vertex = text.IndexOf("\nVe\n", StringComparison.Ordinal);
        int point = text.IndexOf('\n', vertex + 4) + 1;
        text = string.Concat(text.AsSpan(0, point), "5 5 5", text.AsSpan(text.IndexOf('\n', point)));
        return _kernel.Deserialize(Encoding.ASCII.GetBytes(text));
    }
}
