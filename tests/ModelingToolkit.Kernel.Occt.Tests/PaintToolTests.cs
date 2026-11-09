using ModelingToolkit.Core.Editing.Tools;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class PaintToolTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();
    private readonly LineToolTests.Host _host;
    private readonly PaintTool _tool = new();

    public PaintToolTests()
    {
        _host = new LineToolTests.Host(_document);
        _tool.Activate(_host);
    }

    private static ToolInput OnFace(BrepEntity entity, int face, ToolModifiers modifiers = ToolModifiers.None) =>
        new(new ToolPick([entity], new ElementReference([entity], ElementKind.Face, face), default, Vector3d.UnitZ), modifiers);

    [Fact]
    public void A_face_is_painted_and_the_new_material_added_in_one_command()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        _document.Root.Entities.Add(box);
        var red = new Material("Red");
        _tool.Material = red;
        Assert.Contains("Red", _tool.Hint, StringComparison.Ordinal);

        _tool.Click(OnFace(box, 2));

        Assert.Same(red, box.MaterialOf(2));
        Assert.Single(box.FaceMaterials);
        Assert.Contains(red, _document.Materials);
        Assert.Equal(1, _host.Session.UndoCount());
        _host.Session.Undo();
        Assert.Empty(box.FaceMaterials);
        Assert.DoesNotContain(red, _document.Materials);
    }

    [Fact]
    public void Shift_paints_the_whole_shape_and_a_mesh_takes_the_material()
    {
        var box = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        var mesh = new MeshEntity([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new Triangle(0, 1, 2)]);
        _document.Root.Entities.Add(box);
        _document.Root.Entities.Add(mesh);
        var blue = new Material("Blue");
        _document.Materials.Add(blue);
        _tool.Material = blue;

        _tool.Click(OnFace(box, 0, ToolModifiers.Shift));
        _tool.Click(new ToolInput(new ToolPick([mesh], null, default, default)));

        Assert.Equal(6, box.FaceMaterials.Count);
        Assert.Same(blue, mesh.Material);
        _tool.Material = null;
        _tool.Click(OnFace(box, 0));
        Assert.Equal(5, box.FaceMaterials.Count);
    }
}
