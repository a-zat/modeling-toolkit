using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Tests;

public sealed class AnimationsViewModelTests
{
    private readonly Document _document = new();
    private readonly BlockReference _door;
    private readonly BlockReference _drawer;
    private readonly Animation _openDoor;
    private readonly Animation _openDrawer;
    private readonly Animation _openAll;

    public AnimationsViewModelTests()
    {
        var cabinet = new Block("Cabinet");
        _document.Blocks.Add(cabinet);
        var part = new Block("Part");
        _document.Blocks.Add(part);
        _door = new BlockReference(part, Transform.Identity) { Name = "Door" };
        _drawer = new BlockReference(part, Transform.Translation(0, 0, 100)) { Name = "Drawer" };
        cabinet.Entities.Add(_door);
        cabinet.Entities.Add(_drawer);
        _document.Root.Entities.Add(new BlockReference(cabinet, Transform.Identity));
        _document.Root.Entities.Add(new BlockReference(part, Transform.Identity) { Name = "Table" });

        _openDoor = new Animation("Open_door", [Slide(_door)]);
        _openDrawer = new Animation("Open_drawer", [Slide(_drawer)]);
        _openAll = new Animation("Open_all", [Slide(_door), Slide(_drawer)]);
        _document.Animations.AddRange([_openAll, _openDoor, _openDrawer]);
    }

    [Fact]
    public void The_selection_shows_the_animations_of_its_parts_and_of_the_parts_holding_it()
    {
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);

        // Named after the component moved, or after the animation when it moves several.
        Assert.Equal(["Open all", "Door", "Drawer"], animations.Items.Select(item => item.Name));

        animations.Filter([_document.Root.Entities[0], _door]);
        Assert.Equal([_openAll, _openDoor], animations.Items.Select(item => item.Animation));

        animations.Filter([_document.Root.Entities[0]]);
        Assert.Equal(3, animations.Items.Count);

        animations.Filter([_document.Root.Entities[1]]);
        Assert.False(animations.HasItems);
        Assert.True(animations.HasAnimations);
    }

    [Fact]
    public void Playing_moves_the_parts_in_the_pose_and_leaves_the_document_as_it_is()
    {
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);
        AnimationItemViewModel door = animations.Items[1];
        Assert.Empty(animations.Pose);

        door.Position = 0.5;
        Assert.Equal(Transform.Translation(250, 0, 0), animations.Pose[_door]);
        Assert.False(animations.Pose.ContainsKey(_drawer));
        Assert.True(door.IsOpen);

        door.IsOpen = false;
        animations.Advance(0.25); // a quarter of its two seconds
        Assert.Equal(0.375, door.Position, 1e-9);
        animations.Advance(10);
        Assert.Equal(0, door.Position);
        Assert.False(animations.IsPlaying);
        Assert.Equal(Transform.Identity, _door.Transform);
    }

    [Fact]
    public void An_animation_holding_all_the_parts_of_others_moves_them_along()
    {
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);
        AnimationItemViewModel all = animations.Items[0], door = animations.Items[1], drawer = animations.Items[2];

        all.Position = 1;

        Assert.Equal((1.0, true), (door.Position, door.IsOpen));
        Assert.Equal((1.0, true), (drawer.Position, drawer.IsOpen));
        Assert.Equal(Transform.Translation(500, 0, 100), animations.Pose[_drawer]);

        door.Position = 0; // the latest moved places the door
        Assert.Equal(Transform.Identity, animations.Pose[_door]);
        Assert.Equal(1.0, all.Position);
    }

    [Fact]
    public void A_click_toggles_the_animation_of_the_innermost_part_moved()
    {
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);

        Assert.True(animations.ToggleAt([_document.Root.Entities[0], _drawer]));
        animations.Advance(10);

        Assert.True(animations.Items[2].IsOpen);
        Assert.False(animations.Items[1].IsOpen);
        Assert.False(animations.ToggleAt([_document.Root.Entities[1]]));
    }

    [Fact]
    public void All_the_movements_of_the_selection_move_together()
    {
        var table = (BlockReference)_document.Root.Entities[1];
        _document.Animations.Add(new Animation("Move_table", [Slide(table)]));
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);
        animations.Filter([_document.Root.Entities[0]]);
        Assert.True(animations.HasSeveral);

        animations.All.Position = 0.5;
        Assert.All(animations.Items, item => Assert.Equal(0.5, item.Position));
        Assert.False(animations.Pose.ContainsKey(table));

        animations.All.Position = 0;
        animations.All.IsOpen = true;
        animations.Advance(10);
        Assert.All(animations.Items, item => Assert.Equal(1, item.Position));

        animations.Filter([_document.Root.Entities[1]]);
        Assert.Equal("Table", Assert.Single(animations.Items).Name);
        Assert.False(animations.HasSeveral);
    }

    [Fact]
    public void The_switch_for_all_leaves_out_a_movement_that_moves_more_than_the_selection()
    {
        var cabinet = (BlockReference)_document.Root.Entities[0];
        var table = (BlockReference)_document.Root.Entities[1];
        var everything = new Animation("Move_everything", [Slide(cabinet), Slide(table)]);
        _document.Animations.Add(everything);
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);

        animations.Filter([cabinet]);
        Assert.Contains(animations.Items, item => item.Animation == everything); // it moves the cabinet, so it is listed
        Assert.DoesNotContain(animations.All.Items, item => item.Animation == everything);
        Assert.Equal(3, animations.All.Items.Count); // the door, the drawer and both together

        animations.All.IsOpen = true;
        animations.Advance(10);
        Assert.False(animations.Items.Single(item => item.Animation == everything).IsOpen);

        animations.Filter(null); // the model: all of them
        Assert.Contains(animations.All.Items, item => item.Animation == everything);
    }

    [Fact]
    public void Starting_a_movement_stops_another_that_shares_a_part_without_holding_it_all()
    {
        // Open door and slide a pair of the door and the table share the door, and neither holds the other's parts.
        var table = (BlockReference)_document.Root.Entities[1];
        var doorAndTable = new Animation("Door_and_table", [Slide(_door), Slide(table)]);
        _document.Animations.Add(doorAndTable);
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);
        AnimationItemViewModel pair = animations.Items.Single(item => item.Animation == doorAndTable);
        AnimationItemViewModel all = animations.Items.Single(item => item.Animation == _openAll);

        pair.Position = 1;
        all.Position = 0.5; // shares the door with the pair, which neither holds the other

        Assert.Equal((0.0, false), (pair.Position, pair.IsOpen));
        Assert.False(animations.Pose.ContainsKey(table));
        Assert.Equal(Transform.Translation(250, 0, 0), animations.Pose[_door]);
    }

    [Fact]
    public void A_click_on_the_table_opens_the_leaf_hidden_nearest_to_it()
    {
        // A table of 1800 mm with a leaf scaled to nothing at each end, which scale up to be seen: nothing to click.
        var document = new Document();
        var leaf = new Block("Leaf");
        leaf.Entities.Add(Plate(0, 450));
        var top = new Block("Top");
        top.Entities.Add(Plate(-900, 900));
        BlockReference right = new(leaf, Transform.Translation(900, 0, 0) * Transform.Scaling(default, 1e-6)) { Name = "Leaf_right" };
        BlockReference left = new(leaf, Transform.Translation(-900, 0, 0) * Transform.Scaling(default, 1e-6)) { Name = "Leaf_left" };
        top.Entities.Add(right);
        top.Entities.Add(left);
        document.Blocks.AddRange([leaf, top]);
        var table = new BlockReference(top, Transform.Identity);
        document.Root.Entities.Add(table);
        Animation Grow(BlockReference target) => new($"Open_{target.Name}", [new AnimationChannel(
            target, AnimatedProperty.Scale, AnimationInterpolation.Linear, [0, 1], [1e-6, 1e-6, 1e-6, 1, 1, 1])]);
        Animation openRight = Grow(right), openLeft = Grow(left);
        document.Animations.AddRange([openLeft, openRight]);
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(document);
        IReadOnlyList<Entity> path = [table, top.Entities[0]];

        // The click on the top near the right end is for the leaf there; near the left end, for the other; far from
        // both, and with nothing to say where, for none.
        Assert.True(animations.ToggleAt(path, new Point3d(700, 0, 0)));
        Assert.True(animations.Items.Single(item => item.Animation == openRight).IsOpen);
        Assert.False(animations.Items.Single(item => item.Animation == openLeft).IsOpen);
        Assert.True(animations.ToggleAt(path, new Point3d(-700, 0, 0)));
        Assert.True(animations.Items.Single(item => item.Animation == openLeft).IsOpen);
        Assert.False(animations.CanToggleAt(path));
        Assert.False(animations.CanToggleAt(path, new Point3d(0, 5000, 0)));
    }

    [Fact]
    public void A_click_on_what_holds_parts_that_are_not_hidden_opens_nothing_wherever_it_is()
    {
        // The body of the cabinet, with its door and drawer at rest, seen: a click on it beside them opens neither.
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(_document);
        IReadOnlyList<Entity> body = [_document.Root.Entities[0]];

        Assert.False(animations.CanToggleAt(body, new Point3d(0, 0, 0)));
        Assert.False(animations.ToggleAt(body, new Point3d(0, 0, 100)));
        Assert.True(animations.ToggleAt([_document.Root.Entities[0], _door], new Point3d(0, 0, 0)));
    }

    [Fact]
    public void What_follows_an_anchor_of_a_part_that_an_animation_moves_goes_with_it_in_the_pose_only()
    {
        // A leaf that slides out by 500 mm, with an anchor at its end, and a chair that follows it.
        var document = new Document();
        var leafBlock = new Block("Leaf");
        var anchor = new AnchorEntity(Transform.Translation(450, 0, 0));
        leafBlock.Entities.Add(anchor);
        var chairBlock = new Block("Chair");
        document.Blocks.AddRange([leafBlock, chairBlock]);
        var leaf = new BlockReference(leafBlock, Transform.Translation(900, 0, 0));
        var chair = new BlockReference(chairBlock, Transform.Translation(1500, 0, 0));
        document.Root.Entities.Add(leaf);
        document.Root.Entities.Add(chair);
        document.Attach(new Attachment(chair, [leaf], anchor, Attachment.OffsetOf(chair, [leaf], anchor)));
        var slide = new Animation("Slide", [new AnimationChannel(
            leaf, AnimatedProperty.Translation, AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 500, 0, 0])]);
        document.Animations.Add(slide);
        var animations = new AnimationsViewModel(useTimer: false);
        animations.Show(document);

        animations.Items.Single().Position = 1;

        Assert.Equal(2000, animations.Pose[chair].Apply(Point3d.Origin).X, 1e-9);
        Assert.Equal(1500, chair.Transform.Apply(Point3d.Origin).X, 1e-9); // the document as it was
    }

    private static MeshEntity Plate(double from, double to) => new(
        [new(from, -450, 0), new(to, -450, 0), new(to, 450, 0), new(from, 450, 0)], [new(0, 1, 2), new(0, 2, 3)]);

    // 500 mm along X in two seconds.
    private static AnimationChannel Slide(BlockReference target) => new(
        target, AnimatedProperty.Translation, AnimationInterpolation.Linear, [0, 2], [0, 0, 0, 500, 0, 0]);
}
