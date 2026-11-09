using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class EditContextTests
{
    private readonly Document _document = new();
    private readonly Block _car = new("Car");
    private readonly Block _wheel = new("Wheel");
    private readonly BlockReference _carReference;
    private readonly BlockReference _frontWheel;
    private readonly BrepEntity _tyre = new(new NoKernel(), new NoShape());
    private readonly EditContext _context;
    private int _changes;

    public EditContextTests()
    {
        _wheel.Entities.Add(_tyre);
        _frontWheel = new BlockReference(_wheel, Transform.Translation(0, 10, 0));
        _car.Entities.Add(_frontWheel);
        _carReference = new BlockReference(_car, Transform.Translation(100, 0, 0));
        _document.Blocks.Add(_wheel);
        _document.Blocks.Add(_car);
        _document.Root.Entities.Add(_carReference);
        _context = new EditContext(_document);
        _context.Changed += (_, _) => _changes++;
    }

    [Fact]
    public void The_model_is_the_first_context()
    {
        Assert.True(_context.IsModel);
        Assert.Same(_document.Root, _context.Block);
        Assert.Equal(Transform.Identity, _context.Placement);
    }

    [Fact]
    public void Entering_references_edits_their_blocks_where_they_place_them()
    {
        _context.Enter(_carReference);
        _context.Enter(_frontWheel);

        Assert.Equal([_carReference, _frontWheel], _context.Path);
        Assert.Same(_wheel, _context.Block);
        Assert.Equal(Transform.Translation(100, 10, 0), _context.Placement);

        _context.Leave();
        Assert.Same(_car, _context.Block);
        _context.Enter(_frontWheel);
        _context.LeaveAll();
        _context.LeaveAll();
        _context.Leave();
        Assert.True(_context.IsModel);
        Assert.Equal(5, _changes);
    }

    [Fact]
    public void Only_a_reference_of_the_block_being_edited_can_be_entered()
    {
        Assert.Throws<ArgumentException>(() => _context.Enter(_frontWheel)); // in the car, not in the model
        Assert.Throws<ArgumentException>(() => _context.Enter(new BlockReference(_car, Transform.Identity)));
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void A_path_is_seen_from_the_block_being_edited()
    {
        Assert.Equal([_carReference, _frontWheel, _tyre], _context.Within([_carReference, _frontWheel, _tyre]));

        _context.Enter(_carReference);

        Assert.Equal([_frontWheel, _tyre], _context.Within([_carReference, _frontWheel, _tyre]));
        Assert.Null(_context.Within([_carReference])); // the reference entered is not in its block
        Assert.Null(_context.Within([new BlockReference(_car, Transform.Identity), _frontWheel]));
    }

    [Fact]
    public void Removing_a_reference_entered_leaves_its_block()
    {
        _context.Enter(_carReference);
        _context.Enter(_frontWheel);
        _carReference.Transform = Transform.Identity;
        Assert.Equal(2, _context.Path.Count);

        _car.Entities.Remove(_frontWheel);

        Assert.Equal([_carReference], _context.Path);
        _document.Root.Entities.Clear();
        Assert.True(_context.IsModel);
        Assert.Equal(4, _changes);
    }
}
