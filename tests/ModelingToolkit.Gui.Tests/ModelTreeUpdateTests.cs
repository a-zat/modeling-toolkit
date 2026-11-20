using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Gui.ViewModels;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The model tree and the properties follow the changes of the document.</summary>
public sealed class ModelTreeUpdateTests
{
    [Fact]
    public void A_reference_inside_a_block_is_dragged_out_onto_the_project()
    {
        var chair = new Block("Chair") { Entities = { Square() } };
        var room = new Block("Room");
        var inner = new BlockReference(chair, Transform.Translation(10, 0, 0));
        room.Entities.Add(inner);
        var document = new Document();
        document.Blocks.Add(chair);
        document.Blocks.Add(room);
        document.Root.Entities.Add(new BlockReference(room, Transform.Translation(100, 0, 0)));
        var session = new ModelingToolkit.Core.Editing.EditSession(document);
        var tree = new ModelTreeViewModel { Edit = command => { session.Execute(command); return true; } };
        tree.Show(document);
        ModelNodeViewModel dragged = tree.Model!.Children[0].Children[0];

        Assert.True(tree.CanMoveInto(dragged, tree.Model));
        Assert.True(tree.MoveInto(dragged, tree.Model));
        Assert.Contains(inner, document.Root.Entities);
        Assert.Equal(Transform.Translation(110, 0, 0), inner.Transform); // where it was in the project
        session.Undo();

        // From its menu, one level up.
        dragged = tree.Model.Children[0].Children[0];
        Assert.True(dragged.CanMoveOut);
        Assert.Equal("Move out of Room", dragged.MoveOutLabel);
        tree.MoveOutCommand.Execute(dragged);
        Assert.Contains(inner, document.Root.Entities);
        Assert.Same(inner, tree.SelectedNode?.Entity);
        Assert.False(tree.SelectedNode!.CanMoveOut);
    }

    [Fact]
    public void Several_references_selected_move_out_and_are_deleted_together_and_come_back_in_their_order()
    {
        var chair = new Block("Chair") { Entities = { Square() } };
        var room = new Block("Room");
        BlockReference[] chairs = [.. Enumerable.Range(0, 3).Select(i =>
            new BlockReference(chair, Transform.Translation(i * 100, 0, 0)) { Name = $"Chair {i}" })];
        foreach (BlockReference added in chairs)
        {
            room.Entities.Add(added);
        }
        var document = new Document();
        document.Blocks.AddRange([chair, room]);
        document.Root.Entities.Add(new BlockReference(room, Transform.Identity));
        var session = new ModelingToolkit.Core.Editing.EditSession(document);
        var tree = new ModelTreeViewModel { Edit = command => { session.Execute(command); return true; } };
        tree.Show(document);
        IReadOnlyList<ModelNodeViewModel> nodes = tree.Model!.Children[0].Children;
        tree.SelectedNodes.Add(nodes[0]);
        tree.SelectedNodes.Add(nodes[2]);

        tree.MoveOutCommand.Execute(nodes[2]);
        Assert.Equal([chairs[1]], room.Entities);
        Assert.Contains(chairs[0], document.Root.Entities);
        Assert.Contains(chairs[2], document.Root.Entities);
        session.Undo();
        Assert.Equal(chairs, room.Entities); // one undo, each back in its place

        nodes = tree.Model.Children[0].Children;
        tree.SelectedNodes.Clear();
        tree.SelectedNodes.Add(nodes[0]);
        tree.SelectedNodes.Add(nodes[2]);
        tree.DeleteCommand.Execute(nodes[0]);
        Assert.Equal([chairs[1]], room.Entities);
        session.Undo();
        Assert.Equal(chairs, room.Entities);

        // A node that is not among those selected is the only one worked on.
        tree.SelectedNodes.Clear();
        tree.SelectedNodes.Add(tree.Model.Children[0].Children[0]);
        tree.SelectedNodes.Add(tree.Model.Children[0].Children[2]);
        tree.DeleteCommand.Execute(tree.Model.Children[0].Children[1]);
        Assert.Equal([chairs[0], chairs[2]], room.Entities);
    }

    [Fact]
    public void References_selected_go_into_a_new_group_where_they_stay_and_one_reference_is_made_a_group_or_a_component()
    {
        var chair = new Block("Chair") { Entities = { Square() } };
        var document = new Document();
        document.Blocks.Add(chair);
        BlockReference[] chairs = [.. Enumerable.Range(0, 3).Select(i =>
            new BlockReference(chair, Transform.Translation(i * 100, 0, 0)) { Name = $"Chair {i}" })];
        foreach (BlockReference added in chairs)
        {
            document.Root.Entities.Add(added);
        }
        var session = new ModelingToolkit.Core.Editing.EditSession(document);
        var tree = new ModelTreeViewModel { Edit = command => { session.Execute(command); return true; } };
        tree.Show(document);
        tree.SelectedNodes.Add(tree.Model!.Children[0]);
        tree.SelectedNodes.Add(tree.Model.Children[2]);

        Assert.Equal("Group", tree.GroupLabel);
        tree.GroupOrToggleCommand.Execute(tree.Model.Children[0]);
        var group = (BlockReference)document.Root.Entities.Single(entity => entity is BlockReference { IsGroup: true });
        Assert.Equal([chairs[0], chairs[2]], group.Block.Entities);
        Assert.Equal(Transform.Translation(200, 0, 0), chairs[2].Transform); // where it was
        Assert.Same(group, tree.SelectedNode?.Entity);
        Assert.True(tree.SelectedNode!.IsGroup);

        tree.SelectedNodes.Clear(); // as the outline leaves the group alone selected
        // One menu item: "Group" with several selected, else making the one a group or a component.
        Assert.Equal("Convert to component", tree.GroupLabel);
        tree.GroupOrToggleCommand.Execute(tree.SelectedNode);
        Assert.False(group.IsGroup);
        session.Undo(); // a group again
        session.Undo(); // the grouping
        Assert.Equal(chairs, document.Root.Entities);
    }

    [Fact]
    public void A_duplicate_goes_next_to_the_original_placing_the_same_block()
    {
        var chair = new Block("Chair") { Entities = { Square() } };
        var original = new BlockReference(chair, Transform.Translation(10, 0, 0)) { Name = "Chair by the window" };
        var document = new Document();
        document.Blocks.Add(chair);
        document.Root.Entities.Add(original);
        var session = new ModelingToolkit.Core.Editing.EditSession(document);
        var tree = new ModelTreeViewModel { Edit = command => { session.Execute(command); return true; } };
        tree.Show(document);

        tree.DuplicateCommand.Execute(tree.Model!.Children[0]);

        var copy = Assert.IsType<BlockReference>(document.Root.Entities[1]);
        Assert.Same(chair, copy.Block);
        Assert.Equal(original.Transform, copy.Transform);
        Assert.Equal("Chair by the window", copy.Name);
        Assert.Same(copy, tree.SelectedNode?.Entity);
        session.Undo();
        Assert.Single(document.Root.Entities);
    }

    [Fact]
    public void TheOutlineShowsHidesAndRenamesTheEntitiesByCommands()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        var session = new ModelingToolkit.Core.Editing.EditSession(document);
        var tree = new ModelTreeViewModel { Edit = command => { session.Execute(command); return true; } };
        tree.Show(document);
        ModelNodeViewModel node = tree.Model!.Children[0];

        tree.ToggleVisibleCommand.Execute(node);
        Assert.True(reference.IsHidden);
        Assert.False(node.IsVisible);

        tree.StartRenameCommand.Execute(node);
        Assert.True(node.IsRenaming);
        Assert.Equal("Wheel", node.EditedName);
        node.EditedName = "  Front wheel ";
        tree.CommitRenameCommand.Execute(node);
        Assert.False(node.IsRenaming);
        Assert.Equal("Front wheel", reference.Name);
        Assert.Equal("Front wheel", node.Name);

        // Nothing typed, or Escape, leaves the name as it is.
        tree.StartRenameCommand.Execute(node);
        node.EditedName = " ";
        tree.CommitRenameCommand.Execute(node);
        tree.StartRenameCommand.Execute(node);
        node.EditedName = "Other";
        tree.CancelRenameCommand.Execute(node);
        Assert.Equal("Front wheel", reference.Name);

        session.Undo();
        session.Undo();
        Assert.Equal("Wheel", node.Name);
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void AddedAndRemovedEntitiesChangeTheChildrenKeepingTheOtherNodes()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        var tree = new ModelTreeViewModel();
        tree.Show(document);
        ModelNodeViewModel model = tree.Model!;
        ModelNodeViewModel referenceNode = model.Children[0];
        referenceNode.IsExpanded = true;
        _ = referenceNode.Children; // built, as the tree does when it opens a node
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        MeshEntity body = Square();
        document.Root.Entities.Add(body);
        wheel.Entities.Add(Square());

        // The one entity of the model is a node of its own; the two of the wheel are grouped in its geometry.
        Assert.Equal([nameof(ModelNodeViewModel.Children)], changed);
        Assert.Same(referenceNode, model.Children[0]);
        Assert.True(referenceNode.IsExpanded);
        Assert.Same(body, model.Children[1].Entity);
        Assert.Equal([body], model.Children[1].Path);
        ModelNodeViewModel wheelGeometry = Assert.Single(referenceNode.Children);
        Assert.True(wheelGeometry.IsGeometry);
        Assert.Equal("2 meshes", wheelGeometry.Detail);
        Assert.Equal([reference, wheel.Entities[1]], wheelGeometry.Children[1].Path);

        document.Root.Entities.Add(Square());
        ModelNodeViewModel geometry = model.Children[1];
        Assert.True(geometry.IsGeometry);
        Assert.Same(body, geometry.Children[0].Entity);
    }

    [Fact]
    public void ASelectedNodeTakenOutOfTheTreeIsNoLongerSelected()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        document.Root.Entities.Add(Square());
        var tree = new ModelTreeViewModel();
        tree.Show(document);
        tree.SelectCommand.Execute(new Entity[] { reference, wheel.Entities[0] });
        Assert.NotNull(tree.SelectedNode);

        document.Root.Entities.RemoveAt(1);
        Assert.NotNull(tree.SelectedNode);
        document.Root.Entities.Remove(reference);

        Assert.Null(tree.SelectedNode);
    }

    [Fact]
    public void RenamedEntitiesAndBlocksRenameTheirNodes()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        MeshEntity body = Square();
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        document.Root.Entities.Add(body);
        var tree = new ModelTreeViewModel();
        tree.Show(document);
        ModelNodeViewModel model = tree.Model!;

        wheel.Name = "Front wheel";
        body.Material = new Material("Paint");

        Assert.Equal(["Front wheel", "Paint"], model.Children.Select(node => node.Name));

        reference.Name = "Left";
        body.Name = "Body";

        Assert.Equal(["Left", "Body"], model.Children.Select(node => node.Name));
        Assert.Equal("Front wheel", model.Children[0].Detail);
    }

    [Fact]
    public async Task ThePropertiesOfTheSelectedNodeFollowTheChanges()
    {
        var viewModel = new MainWindowViewModel(new DocumentFormats([new GltfReader()], []), new NoAppearance());
        await viewModel.LoadAsync(Paths.Asset("gltf", "Box.glb"));
        Document document = viewModel.Document!;
        Assert.Equal("Project", viewModel.Properties.Kind); // nothing selected
        Assert.Contains(new PropertyRow("Meshes", "1 (1 placed)"), viewModel.Properties.Rows);

        document.Root.Entities.Add(Square());

        Assert.Contains(new PropertyRow("Meshes", "2 (2 placed)"), viewModel.Properties.Rows);

        ModelNodeViewModel squareNode = viewModel.ModelTree.Roots[^1]; // the one geometry of the model
        Assert.IsType<MeshEntity>(squareNode.Entity);
        viewModel.ModelTree.SelectedNode = squareNode;
        squareNode.Entity!.Name = "Floor";

        Assert.Equal("Floor", viewModel.Properties.Title);
    }

    private sealed class NoAppearance : IAppearance
    {
        public void Apply(bool dark)
        {
        }
    }

    private static MeshEntity Square() => new(
        [new Point3d(0, 0, 0), new Point3d(1000, 0, 0), new Point3d(1000, 1000, 0), new Point3d(0, 1000, 0)],
        [new Triangle(0, 1, 2), new Triangle(0, 2, 3)]);
}
