using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Editing;

public sealed class GroupsTests
{
    [Fact]
    public void A_click_goes_through_groups_to_the_component_inside_and_stops_at_a_group_of_loose_meshes()
    {
        var sofa = new Block("Sofa") { Entities = { Mesh() } };
        var room = new Block("Room");
        var house = new Block("House");
        var sofaReference = new BlockReference(sofa, Transform.Identity);
        var roomReference = new BlockReference(room, Transform.Identity) { IsGroup = true };
        var houseReference = new BlockReference(house, Transform.Identity) { IsGroup = true };
        IReadOnlyList<Entity> path = [houseReference, roomReference, sofaReference, sofa.Entities[0]];

        Assert.Equal(2, Groups.Through(path, 0)); // the sofa, past the house and the room
        Assert.Equal(2, Groups.Through(path, 2));

        roomReference.IsGroup = false; // a component: it is what is selected
        Assert.Equal(1, Groups.Through(path, 0));

        IReadOnlyList<Entity> wall = [houseReference, house.Entities.Count > 0 ? house.Entities[0] : Mesh()];
        Assert.Equal(0, Groups.Through(wall, 0)); // a mesh of the group: the group
    }

    [Fact]
    public void Making_a_group_and_undoing_it_raise_a_change_and_copies_keep_it()
    {
        var document = new Document();
        var block = new Block("Room");
        document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Identity);
        document.Root.Entities.Add(reference);
        var changes = new List<DocumentChange>();
        document.Changed += (_, change) => changes.Add(change);

        var command = new SetGroupCommand(reference, true);
        command.Apply();
        Assert.True(reference.IsGroup);
        Assert.Contains(new ModelingToolkit.Core.Model.PropertyChanged(reference, nameof(BlockReference.IsGroup)), changes);
        Assert.True(((BlockReference)TransformCommands.Duplicate(reference, Transform.Identity)).IsGroup);
        command.Revert();
        Assert.False(reference.IsGroup);
    }

    private static MeshEntity Mesh() => new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new(0, 1, 2)]);
}
