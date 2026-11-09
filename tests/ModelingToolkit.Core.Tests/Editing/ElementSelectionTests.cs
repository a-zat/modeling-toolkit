using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class ElementSelectionTests
{
    private readonly Document _document = new();
    private readonly BrepEntity _shape = new(new NoKernel(), new NoShape());
    private readonly Block _part;
    private readonly BlockReference _reference;
    private readonly ElementSelection _selection;
    private int _changes;

    public ElementSelectionTests()
    {
        _part = new Block("Part") { Entities = { _shape } };
        _reference = new BlockReference(_part, Transform.Identity);
        _document.Blocks.Add(_part);
        _document.Root.Entities.Add(_reference);
        _selection = new ElementSelection(_document);
        _selection.Changed += (_, _) => _changes++;
    }

    private ElementReference Face(int index) => new([_reference, _shape], ElementKind.Face, index);

    [Fact]
    public void Select_takes_only_the_element_and_toggle_adds_or_removes_it()
    {
        _selection.Select(Face(0));
        _selection.Toggle(Face(1));
        _selection.Toggle(Face(2));
        _selection.Toggle(Face(1));

        Assert.Equal([Face(0), Face(2)], _selection.Elements);
        Assert.True(_selection.Contains(Face(2)));
        Assert.Equal(4, _changes);

        _selection.Select(Face(1));
        Assert.Equal([Face(1)], _selection.Elements);
        _selection.Select(Face(1));
        _selection.Clear();
        _selection.Clear();
        Assert.Empty(_selection.Elements);
        Assert.Equal(6, _changes);
    }

    [Fact]
    public void Only_the_elements_of_the_document_can_be_selected()
    {
        var other = new BlockReference(_part, Transform.Identity); // not in the model

        Assert.Throws<ArgumentException>(() => _selection.Select(new ElementReference([other, _shape], ElementKind.Edge, 0)));
        Assert.Throws<ArgumentException>(() => _selection.Toggle(new ElementReference([_shape], ElementKind.Edge, 0)));
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void A_replaced_shape_unselects_its_elements()
    {
        var loose = new BrepEntity(new NoKernel(), new NoShape());
        _document.Root.Entities.Add(loose);
        _selection.Select(Face(0));
        _selection.Toggle(new ElementReference([loose], ElementKind.Vertex, 0));

        _shape.Shape = new NoShape();

        Assert.Equal([new ElementReference([loose], ElementKind.Vertex, 0)], _selection.Elements);
        Assert.Equal(3, _changes);
    }

    [Fact]
    public void Elements_whose_path_leaves_the_document_are_unselected()
    {
        _selection.Select(Face(0));
        _reference.Name = "Renamed";
        _reference.Transform = Transform.Translation(5, 0, 0);
        Assert.Equal([Face(0)], _selection.Elements); // still there

        _document.Root.Entities.Remove(_reference);

        Assert.Empty(_selection.Elements);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void Elements_of_a_block_taken_out_of_the_document_are_unselected()
    {
        _selection.Select(Face(0));

        _document.Blocks.Remove(_part);

        Assert.Empty(_selection.Elements);
    }
}
