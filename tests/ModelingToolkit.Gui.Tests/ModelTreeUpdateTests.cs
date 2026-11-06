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
    public void AddedAndRemovedEntitiesChangeTheChildrenKeepingTheOtherNodes()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        var tree = new ModelTreeViewModel();
        tree.Show(document);
        ModelNodeViewModel model = tree.Roots[0];
        ModelNodeViewModel referenceNode = model.Children[0];
        referenceNode.IsExpanded = true;
        _ = referenceNode.Children; // built, as the tree does when it opens a node
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        MeshEntity body = Square();
        document.Root.Entities.Add(body);
        wheel.Entities.Add(Square());

        Assert.Equal([nameof(ModelNodeViewModel.Children)], changed);
        Assert.Same(referenceNode, model.Children[0]);
        Assert.True(referenceNode.IsExpanded);
        Assert.Same(body, model.Children[1].Entity);
        Assert.Equal([body], model.Children[1].Path);
        Assert.Equal(2, referenceNode.Children.Count);
        Assert.Equal([reference, wheel.Entities[1]], referenceNode.Children[1].Path);
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
        ModelNodeViewModel model = tree.Roots[0];

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
        viewModel.ModelTree.SelectedNode = viewModel.ModelTree.Roots[0];
        Assert.Contains(new PropertyRow("Meshes", "1 (1 placed)"), viewModel.Properties.Rows);

        document.Root.Entities.Add(Square());

        Assert.Contains(new PropertyRow("Meshes", "2 (2 placed)"), viewModel.Properties.Rows);

        ModelNodeViewModel squareNode = viewModel.ModelTree.Roots[0].Children[^1];
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
