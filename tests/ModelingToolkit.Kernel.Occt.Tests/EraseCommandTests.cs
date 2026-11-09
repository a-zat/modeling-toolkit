using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class EraseCommandTests
{
    private readonly OcctKernel _kernel = new();
    private readonly Document _document = new();

    private BrepEntity Add(IKernelShape shape)
    {
        var entity = new BrepEntity(_kernel, shape);
        _document.Root.Entities.Add(entity);
        return entity;
    }

    [Fact]
    public void Erasing_a_face_of_a_box_leaves_an_open_box_whose_faces_keep_their_materials()
    {
        BrepEntity box = Add(_kernel.MakeBox(100, 100, 100));
        IReadOnlyList<Entity> path = [box];
        int top = Enumerable.Range(0, 6).Single(f => _kernel.GetFaceInfo(box.Shape, f).Plane is { Normal.Z: > 0.5 });
        int bottom = Enumerable.Range(0, 6).Single(f => _kernel.GetFaceInfo(box.Shape, f).Plane is { Normal.Z: < -0.5 });
        var red = new Material("Red");
        box.Paint([bottom], red);
        var session = new EditSession(_document);

        session.Execute(EraseCommand.Of([new ElementReference(path, ElementKind.Face, top)])!);

        Assert.Equal(new ShapeTopology(0, 5, 12, 8), _kernel.CountElements(box.Shape));
        int newBottom = Enumerable.Range(0, 5).Single(f => _kernel.GetFaceInfo(box.Shape, f).Plane is { Normal.Z: < -0.5 });
        Assert.Same(red, box.MaterialOf(newBottom));
        Assert.Single(box.FaceMaterials);
        session.Undo();
        Assert.Equal(new ShapeTopology(1, 6, 12, 8), _kernel.CountElements(box.Shape));
    }

    [Fact]
    public void Erasing_every_edge_deletes_the_shape_until_undone()
    {
        LineCurve[] square =
        [
            new(new(0, 0, 0), new(100, 0, 0)), new(new(100, 0, 0), new(100, 100, 0)),
            new(new(100, 100, 0), new(0, 100, 0)), new(new(0, 100, 0), new(0, 0, 0)),
        ];
        BrepEntity drawing = Add(_kernel.AddEdges(null, square).Shape);
        BrepEntity other = Add(_kernel.MakeBox(10, 10, 10));
        IReadOnlyList<Entity> path = [drawing];
        var session = new EditSession(_document);

        IEditCommand erase = EraseCommand.Of([
            .. Enumerable.Range(0, 4).Select(edge => new ElementReference(path, ElementKind.Edge, edge)),
            new ElementReference([other], ElementKind.Vertex, 0),
        ])!;
        session.Execute(erase);

        Assert.Same(other, Assert.Single(_document.Root.Entities));
        session.Undo();
        Assert.Equal(2, _document.Root.Entities.Count);
        Assert.Null(EraseCommand.Of([new ElementReference(path, ElementKind.Vertex, 0)]));
    }
}
