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
