using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class EntityClipboardTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void An_entity_copied_inside_a_block_is_pasted_at_the_same_place_of_the_model()
    {
        var document = new Document();
        var part = new Block("Part");
        var box = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)) { Name = "Box" };
        part.Entities.Add(box);
        document.Blocks.Add(part);
        var reference = new BlockReference(part, Transform.Translation(100, 0, 0));
        document.Root.Entities.Add(reference);
        var clipboard = new EntityClipboard();
        var context = new EditContext(document);
        Assert.False(clipboard.CanPaste(document));

        clipboard.Copy(document, [reference, box]);
        (IEditCommand command, Entity pasted) = clipboard.Paste(document, context)!.Value;
        new EditSession(document).Execute(command);

        Assert.Same(document.Root, pasted.Owner);
        Assert.Equal(100, pasted.GetBoundingBox().Min.X, precision: 6);
        Assert.False(clipboard.CanPaste(new Document()));
    }

    [Fact]
    public void A_reference_is_not_pasted_into_its_own_block()
    {
        var document = new Document();
        var part = new Block("Part");
        document.Blocks.Add(part);
        var reference = new BlockReference(part, Transform.Identity);
        document.Root.Entities.Add(reference);
        var clipboard = new EntityClipboard();
        var context = new EditContext(document);
        clipboard.Copy(document, [reference]);
        context.Enter(reference);

        Assert.Null(clipboard.Paste(document, context));
    }
}
