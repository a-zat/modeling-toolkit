using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Model;

public sealed class FaceMaterialsTests
{
    private readonly Document _document = new();
    private readonly BrepEntity _shape = new(new NoKernel(), new NoShape());
    private readonly List<DocumentChange> _changes = [];
    private readonly Material _red = new("Red");
    private readonly Material _blue = new("Blue");

    public FaceMaterialsTests()
    {
        _document.Root.Entities.Add(_shape);
        _document.Changed += (_, change) => _changes.Add(change);
    }

    [Fact]
    public void Painting_faces_gives_them_a_material_and_null_takes_it_away()
    {
        _shape.Paint([0, 2], _red);
        _shape.Paint([2], _red); // already red
        _shape.Paint([2, 5], null);

        Assert.Same(_red, _shape.MaterialOf(0));
        Assert.Null(_shape.MaterialOf(2));
        Assert.Equal([0], _shape.FaceMaterials.Keys);
        Assert.Equal<DocumentChange>(
            [new PropertyChanged(_shape, nameof(BrepEntity.FaceMaterials)), new PropertyChanged(_shape, nameof(BrepEntity.FaceMaterials))],
            _changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => _shape.Paint([-1], _red));
    }

    [Fact]
    public void Painting_is_undone_face_by_face()
    {
        _shape.Paint([0], _blue);
        var session = new EditSession(_document);

        session.Execute(new PaintFacesCommand(_shape, [0, 1, 1], _red));
        Assert.Equal("Paint", session.UndoName);
        Assert.Same(_red, _shape.MaterialOf(1));

        session.Undo();
        Assert.Same(_blue, _shape.MaterialOf(0));
        Assert.Null(_shape.MaterialOf(1));
    }

    [Fact]
    public void A_new_shape_has_the_materials_given_for_its_faces_and_undo_brings_the_old_ones_back()
    {
        _shape.Paint([0, 1], _red);
        _changes.Clear();

        var replacement = new ReplaceShapeCommand("Push", _shape, new NoShape(), new Dictionary<int, Material> { [4] = _blue });
        replacement.Apply();
        Assert.Equal([4], _shape.FaceMaterials.Keys);
        Assert.IsType<ShapeReplaced>(Assert.Single(_changes));

        replacement.Revert();
        Assert.Equal([0, 1], _shape.FaceMaterials.Keys.Order());
    }

    [Fact]
    public void A_shape_edited_by_the_kernel_paints_each_face_as_the_face_it_comes_from()
    {
        _shape.Paint([0], _red);
        _shape.Paint([1], _blue);
        var session = new EditSession(_document);

        // Face 0 split into faces 0 and 2, face 1 now 3, face 1 made by the edit.
        session.Execute(new ReplaceShapeCommand("Line", _shape, new ShapeEdit(new NoShape(), [0, -1, 0, 1])));

        Assert.Same(_red, _shape.MaterialOf(0));
        Assert.Null(_shape.MaterialOf(1));
        Assert.Same(_red, _shape.MaterialOf(2));
        Assert.Same(_blue, _shape.MaterialOf(3));
        session.Undo();
        Assert.Same(_blue, _shape.MaterialOf(1));
        Assert.Equal([0, 1], _shape.FaceMaterials.Keys.Order());
    }

    [Fact]
    public void A_shape_replaced_without_materials_has_its_faces_unpainted()
    {
        _shape.Paint([0], _red);

        _shape.Shape = new NoShape();

        Assert.Empty(_shape.FaceMaterials);
    }
}
