using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The scene follows the changes of the document, changing in the viewer only the instances concerned.</summary>
public sealed class ViewportSceneUpdateTests
{
    private readonly OcctKernel _kernel = new();

    [Fact]
    public void AnAddedEntityAddsItsInstancesAndLeavesTheOthersAsTheyAre()
    {
        var document = new Document();
        document.Root.Entities.Add(Square());
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        MeshEntity added = Square();
        document.Root.Entities.Insert(0, added);
        scene.Update();

        Assert.Equal(["add mesh 1", "add instance 1"], viewer.Changes);
        Assert.Equal([added], scene.PathOfShown(1));
        Assert.Equal(2, scene.Instances.Count);
    }

    [Fact]
    public void ARemovedEntityRemovesItsInstancesAndReleasesItsMesh()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var left = new BlockReference(wheel, Transform.Translation(-500, 0, 0));
        var right = new BlockReference(wheel, Transform.Translation(500, 0, 0));
        MeshEntity body = Square();
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(left);
        document.Root.Entities.Add(right);
        document.Root.Entities.Add(body);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        document.Root.Entities.Remove(left);
        document.Root.Entities.Remove(body);
        scene.Update();

        Assert.Equal(["remove instance 0", "remove instance 2", "remove mesh 1"], viewer.Changes);
        Assert.Null(scene.Meshes[1]);
        Assert.Equal(0..1, scene.InstancesOf([right]));
        Assert.Equal([right, wheel.Entities[0]], scene.PathOfShown(1));
    }

    [Fact]
    public void AMovedReferenceChangesTheInstancesItPlacesOnly()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var left = new BlockReference(wheel, Transform.Translation(-500, 0, 0));
        var right = new BlockReference(wheel, Transform.Translation(500, 0, 0));
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(left);
        document.Root.Entities.Add(right);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        right.Transform = Transform.Translation(700, 0, 0);
        scene.Update();

        Assert.Equal(["set instance 1"], viewer.Changes);
        Assert.Equal(Transform.Translation(700, 0, 0), viewer.Instances[1].Placement);
    }

    [Fact]
    public void AMaterialByBlockReachesTheInstancesOfTheReference()
    {
        var wheel = new Block("Wheel") { Entities = { Square(), Square(new Material("Own")) } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);
        var red = new Material("Red");

        reference.Material = red;
        scene.Update();

        Assert.Equal(["set instance 0"], viewer.Changes);
        Assert.Same(red, viewer.Materials[viewer.Instances[0].Material]);
    }

    [Fact]
    public void NothingChangesInTheViewerWhenTheInstancesAreTheSame()
    {
        var document = new Document();
        MeshEntity square = Square();
        document.Root.Entities.Add(square);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        square.Name = "Renamed";
        scene.Update();

        Assert.Empty(viewer.Changes);
    }

    [Fact]
    public void AReplacedShapeKeepsItsTessellationUntilTheNewOneIsAccepted()
    {
        var document = new Document();
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        document.Root.Entities.Add(brep);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);
        IKernelShape taller = _kernel.MakeBox(10, 10, 30);

        brep.Shape = taller;
        scene.Update();
        Assert.Empty(viewer.Changes);
        var (entity, shape) = Assert.Single(scene.TakeShapesToTessellate());
        Assert.Empty(scene.TakeShapesToTessellate()); // already being tessellated

        Assert.True(scene.Accept(ViewportScene.Tessellate(entity, shape)));
        scene.Update();

        Assert.Equal(["add mesh 1", "set instance 0", "remove mesh 0"], viewer.Changes);
        Assert.Equal(30, viewer.Meshes[viewer.Instances[0].Mesh].GetBoundingBox().Max.Z, tolerance: 1e-6);
    }

    [Fact]
    public void ATessellationOfAShapeReplacedAgainIsDiscarded()
    {
        var document = new Document();
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        document.Root.Entities.Add(brep);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        brep.Shape = _kernel.MakeBox(10, 10, 20);
        var first = Assert.Single(scene.TakeShapesToTessellate());
        brep.Shape = _kernel.MakeBox(10, 10, 30);
        var second = Assert.Single(scene.TakeShapesToTessellate());

        Assert.False(scene.Accept(ViewportScene.Tessellate(first.Entity, first.Shape)));
        Assert.True(scene.Accept(ViewportScene.Tessellate(second.Entity, second.Shape)));
        scene.Update();

        Assert.Equal(30, viewer.Meshes[viewer.Instances[0].Mesh].GetBoundingBox().Max.Z, tolerance: 1e-6);
        Assert.Empty(scene.TakeShapesToTessellate());
    }

    [Fact]
    public void AnAddedShapeIsShownOnceTessellated()
    {
        var document = new Document();
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        var brep = new BrepEntity(_kernel, _kernel.MakeSphere(10));
        document.Root.Entities.Add(brep);
        scene.Update();
        Assert.Empty(viewer.Instances);

        var (entity, shape) = Assert.Single(scene.TakeShapesToTessellate());
        Assert.True(scene.Accept(ViewportScene.Tessellate(entity, shape)));
        scene.Update();

        Assert.Equal(["add mesh 0", "add instance 0"], viewer.Changes);
        Assert.Equal([brep], scene.PathOfShown(0));
    }

    [Fact]
    public void AShapeThatCannotBeTessellatedAnyMoreDisappearsWithAWarning()
    {
        var kernel = new FailingKernel();
        var document = new Document();
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10)) { Name = "Box" };
        document.Root.Entities.Add(brep);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        var replacement = new BrepEntity(kernel, kernel.MakeBox(1, 1, 1)) { Name = "Box" };
        document.Root.Entities[0] = replacement;
        scene.Update();
        var (entity, shape) = Assert.Single(scene.TakeShapesToTessellate());
        ShapeMesh result = ViewportScene.Tessellate(entity, shape);
        Assert.True(scene.Accept(result));
        scene.Update();

        Assert.Equal($"Box cannot be shown: {FailingKernel.Reason}", result.Warning);
        Assert.Empty(viewer.Instances);
        Assert.Empty(viewer.Meshes);
    }

    [Fact]
    public void TheSelectionIsShownWithTheInstancesThatRemain()
    {
        var wheel = new Block("Wheel") { Entities = { Square(), Square() } };
        var reference = new BlockReference(wheel, Transform.Identity);
        var document = new Document();
        document.Blocks.Add(wheel);
        document.Root.Entities.Add(reference);
        document.Root.Entities.Add(Square());
        (ViewportScene scene, RecordingViewer viewer) = Show(document);

        wheel.Entities.RemoveAt(0);
        scene.Update();
        scene.ShowSelectionIn(viewer, [reference]);

        Assert.Equal([1], viewer.Selection);
    }

    private static (ViewportScene Scene, RecordingViewer Viewer) Show(Document document)
    {
        var scene = ViewportScene.Of(document);
        var viewer = new RecordingViewer();
        scene.ShowIn(viewer, scene.Textures.Select(_ => (TextureImage?)null).ToArray());
        viewer.Forget();
        return (scene, viewer);
    }

    private static MeshEntity Square(Material? material = null) => new(
        [new Point3d(0, 0, 0), new Point3d(1000, 0, 0), new Point3d(1000, 1000, 0), new Point3d(0, 1000, 0)],
        [new Triangle(0, 1, 2), new Triangle(0, 2, 3)])
    {
        Material = material,
    };
}
