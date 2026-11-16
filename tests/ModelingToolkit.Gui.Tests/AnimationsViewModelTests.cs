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

        Assert.Equal(["Open all", "Open door", "Open drawer"], animations.Items.Select(item => item.Name));

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

    // 500 mm along X in two seconds.
    private static AnimationChannel Slide(BlockReference target) => new(
        target, AnimatedProperty.Translation, AnimationInterpolation.Linear, [0, 2], [0, 0, 0, 500, 0, 0]);
}
