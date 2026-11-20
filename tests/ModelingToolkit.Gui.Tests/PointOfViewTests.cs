using Avalonia.Input;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.IO.Native;
using ModelingToolkit.Kernel.Occt;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Gui.Tests;

public sealed class PointOfViewTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-pov-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // A chair with a point of view over its seat, at (2000, 0), and a door that turns open, at (0, 3000).
    private async Task<MainWindowViewModel> OpenRoomAsync()
    {
        var document = new Document();
        var chair = new Block("Chair");
        chair.Entities.Add(Plate(450));
        chair.Entities.Add(new AnchorEntity(
            Transform.Translation(0, 0, 1200) * Transform.Rotation(Point3d.Origin, Vector3d.UnitZ, Math.PI / 2),
            AnchorKind.PointOfView) { Name = "Sitting" });
        var leaf = new Block("Leaf");
        leaf.Entities.Add(Plate(1000));
        document.Blocks.AddRange([chair, leaf]);
        document.Root.Entities.Add(new BlockReference(chair, Transform.Translation(2000, 0, 0)) { Name = "Chair" });
        var door = new BlockReference(leaf, Transform.Translation(0, 3000, 0)) { Name = "Door" };
        document.Root.Entities.Add(door);
        document.Animations.Add(new Animation("Open_door", [new AnimationChannel(door, AnimatedProperty.Rotation,
            AnimationInterpolation.Linear, [0, 1], [0, 0, 0, 1, 0, 0, 0.7071068, 0.7071068])]));
        string path = Path.Combine(_folder, "room.mtk");
        new NativeDocumentWriter().Write(document, path);

        var viewModel = new MainWindowViewModel(
            new DocumentFormats([new GltfReader(), new NativeDocumentReader(new OcctKernel())], [new NativeDocumentWriter()]),
            new NoAppearance());
        await viewModel.LoadAsync(path);
        return viewModel;
    }

    private static MeshEntity Plate(double z) => new(
        [new(-200, -200, z), new(200, -200, z), new(200, 200, z), new(-200, 200, z)], [new(0, 1, 2), new(0, 2, 3)]);

    private static IReadOnlyList<Entity> MeshOf(MainWindowViewModel viewModel, string component)
    {
        Entity reference = viewModel.Document!.Root.Entities.First(entity => entity.Name == component);
        return [reference, ((BlockReference)reference).Block.Entities.OfType<MeshEntity>().First()];
    }

    private static void Click(MainWindowViewModel viewModel, IReadOnlyList<Entity> path, Point3d point) =>
        viewModel.ViewInputCommand.Execute(new ViewportPick(path, null, point, new System.Numerics.Vector3(0, 0, 1),
            KeyModifiers.None, 1, ViewportInputKind.Click));

    [Fact]
    public async Task A_click_on_a_chair_while_walking_looks_from_its_seat_and_a_click_escape_or_a_new_walk_stands_up()
    {
        MainWindowViewModel viewModel = await OpenRoomAsync();
        viewModel.ToggleWalkCommand.Execute(null);
        Click(viewModel, MeshOf(viewModel, "Door"), new Point3d(500, 500, 0));
        Assert.True(viewModel.IsWalking);

        Click(viewModel, MeshOf(viewModel, "Chair"), new Point3d(2100, 0, 450));
        WalkSeat seat = Assert.IsType<WalkSeat>(viewModel.Seat);
        Assert.Equal(new Point3d(2000, 0, 1200), seat.Eye);
        Assert.Equal(Math.PI / 2, seat.Yaw, 1e-9);

        Click(viewModel, MeshOf(viewModel, "Chair"), new Point3d(2000, 100, 450));
        Assert.Null(viewModel.Seat);

        Click(viewModel, MeshOf(viewModel, "Chair"), new Point3d(2000, 100, 450));
        Assert.NotNull(viewModel.Seat);
        viewModel.EscapeCommand.Execute(null); // stands up, still walking
        Assert.Null(viewModel.Seat);
        Assert.True(viewModel.IsWalking);
    }

    [Fact]
    public async Task Yellow_dots_mark_what_is_at_rest_when_asked_for_and_go_once_it_is_used()
    {
        MainWindowViewModel viewModel = await OpenRoomAsync();
        viewModel.ToggleWalkCommand.Execute(null);
        Click(viewModel, MeshOf(viewModel, "Door"), new Point3d(500, 500, 0));
        Assert.Empty(viewModel.Beacons); // not asked for

        viewModel.ShowsUsable = true;
        Assert.Equal(2, viewModel.Beacons.Count);
        Assert.Contains(viewModel.Beacons, beacon => beacon.Point == new Point3d(0, 3000, 1000)); // in the door
        Assert.Contains(viewModel.Beacons, beacon => beacon.Point.X == 2000 && beacon.Point.Z < 1200); // over the seat

        Click(viewModel, MeshOf(viewModel, "Door"), new Point3d(0, 3000, 1000)); // opens it
        Assert.Single(viewModel.Beacons);
        Click(viewModel, MeshOf(viewModel, "Chair"), new Point3d(2000, 0, 450)); // sits
        Assert.Empty(viewModel.Beacons);

        viewModel.EscapeCommand.Execute(null);
        viewModel.EscapeCommand.Execute(null); // out of the walk
        Assert.Empty(viewModel.Beacons);
    }

    [Fact]
    public async Task The_menu_of_a_component_adds_an_anchor_where_clicked_and_attaches_another_component_to_it()
    {
        MainWindowViewModel viewModel = await OpenRoomAsync();
        IReadOnlyList<ContextAction> actions = viewModel.ContextActions(new ViewportPick(
            MeshOf(viewModel, "Door"), null, new Point3d(100, 3100, 1000), new System.Numerics.Vector3(0, 0, 1),
            KeyModifiers.None, 1, ViewportInputKind.Click));
        ContextAction add = actions.First(action => action.Header == "Add anchor here");
        add.Command!.Execute(add.Parameter);

        var door = (BlockReference)viewModel.Document!.Root.Entities.First(entity => entity.Name == "Door");
        AnchorEntity anchor = Assert.Single(door.Block.Entities.OfType<AnchorEntity>());
        Assert.Equal(new Point3d(100, 100, 1000), anchor.Point); // in the block of the door

        // The chair, brought near it, is offered to follow it; then it moves with the door.
        var chair = (BlockReference)viewModel.Document.Root.Entities.First(entity => entity.Name == "Chair");
        chair.Transform = Transform.Translation(500, 2000, 0);
        IReadOnlyList<ContextAction> chairActions = viewModel.ContextActions(new ViewportPick(
            MeshOf(viewModel, "Chair"), null, new Point3d(500, 2000, 450), new System.Numerics.Vector3(0, 0, 1),
            KeyModifiers.None, 1, ViewportInputKind.Click));
        ContextAction attach = chairActions.First(action => action.Header.StartsWith("Attach to", StringComparison.Ordinal));
        attach.Command!.Execute(attach.Parameter);
        Assert.Single(viewModel.Document.Attachments);

        door.Transform = Transform.Translation(0, 3500, 0);
        Assert.Equal(500, chair.Transform.Apply(Point3d.Origin).X, 1e-9);
        Assert.Equal(2500, chair.Transform.Apply(Point3d.Origin).Y, 1e-9);

        viewModel.UndoCommand.Execute(null); // the attachment
        Assert.Empty(viewModel.Document.Attachments);
    }

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }
}
