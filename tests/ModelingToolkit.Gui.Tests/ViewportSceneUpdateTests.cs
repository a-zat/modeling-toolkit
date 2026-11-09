using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Gui.Viewport.Interop;
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
    public void TheTexturesOfFacesRepeatEveryTextureSize()
    {
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(500, 500, 500));
        var document = new Document();
        document.Root.Entities.Add(brep);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);
        var tiles = new Material("Tiles") { BaseColorTexture = new Texture("tiles", "image/png", [0]), TextureSize = 100 };

        brep.Paint([0], tiles);
        scene.Update();

        ShapeTessellation tessellation = _kernel.Tessellate(brep.Shape, 0.5);
        MeshEntity shown = viewer.Meshes[1];
        int vertex = tessellation.Mesh.Triangles[tessellation.TrianglesOf(0).Start].A;
        int other = tessellation.Mesh.Triangles[tessellation.TrianglesOf(1).Start].A;
        Assert.Equal(tessellation.Mesh.TextureCoordinates![vertex] / 100, shown.TextureCoordinates![vertex]);
        Assert.Equal(tessellation.Mesh.TextureCoordinates[other], shown.TextureCoordinates[other]); // not textured
        // The face, 500 mm wide, is 5 repetitions of the texture across.
        System.Numerics.Vector2[] face = [.. tessellation.Mesh.Triangles[tessellation.TrianglesOf(0)]
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Select(corner => shown.TextureCoordinates[corner])];
        Assert.Equal(5, face.Max(uv => uv.X) - face.Min(uv => uv.X), precision: 3);
        Assert.Equal(5, face.Max(uv => uv.Y) - face.Min(uv => uv.Y), precision: 3);
    }

    [Fact]
    public void PaintingFacesShowsTheShapeWithAMeshOfItsMaterials()
    {
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        var part = new Block("Part") { Entities = { brep } };
        var document = new Document();
        document.Blocks.Add(part);
        document.Root.Entities.Add(new BlockReference(part, Transform.Identity));
        document.Root.Entities.Add(new BlockReference(part, Transform.Translation(20, 0, 0)));
        (ViewportScene scene, RecordingViewer viewer) = Show(document);
        var red = new Material("Red");

        brep.Paint([2, 4], red);
        scene.Update();

        // Both placements show the painted faces, which the block holds; the tessellation is not made again.
        Assert.Equal(["add mesh 1", "set instance 0", "set instance 1", "remove mesh 0"], viewer.Changes);
        int[] faces = viewer.FaceMaterials[1];
        Assert.Same(red, viewer.Materials[faces[2]]);
        Assert.Equal([-1, -1, faces[2], -1, faces[2], -1], faces);
        Assert.Empty(scene.TakeShapesToTessellate());

        viewer.Forget();
        brep.Paint([2, 4], null);
        scene.Update();
        Assert.Equal(["add mesh 2", "set instance 0", "set instance 1", "remove mesh 1"], viewer.Changes);
        Assert.False(viewer.FaceMaterials.ContainsKey(2));
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

    [Fact]
    public void APickOnAShapeIsAReferenceToItsElement()
    {
        var brep = new BrepEntity(_kernel, _kernel.MakeBox(10, 10, 10));
        var block = new Block("Part") { Entities = { brep } };
        var reference = new BlockReference(block, Transform.Translation(100, 0, 0));
        MeshEntity square = Square();
        var document = new Document();
        document.Blocks.Add(block);
        document.Root.Entities.Add(square);
        document.Root.Entities.Add(reference);
        (ViewportScene scene, RecordingViewer _) = Show(document);

        ElementReference? edge = scene.ElementOf(new ViewerPick(1, NativeElementKind.Edge, 4, default, default));

        Assert.Equal(new ElementReference([reference, brep], ElementKind.Edge, 4), edge);
        Assert.Null(scene.ElementOf(new ViewerPick(0, NativeElementKind.Instance, 0, default, default))); // a mesh
        Assert.Null(scene.ElementOf(new ViewerPick(-1, NativeElementKind.Instance, 0, default, default)));
    }

    [Fact]
    public void TheHighlightedElementsAreThoseTheSceneShows()
    {
        var brep = new BrepEntity(_kernel, _kernel.MakeCylinder(5, 10)); // its seam has no polyline
        var block = new Block("Part") { Entities = { brep } };
        var left = new BlockReference(block, Transform.Identity);
        var right = new BlockReference(block, Transform.Translation(100, 0, 0));
        var document = new Document();
        document.Blocks.Add(block);
        document.Root.Entities.Add(left);
        document.Root.Entities.Add(right);
        (ViewportScene scene, RecordingViewer viewer) = Show(document);
        ShapeTessellation tessellation = _kernel.Tessellate(brep.Shape, 0.1);
        int seam = Enumerable.Range(0, 3).Single(index => !tessellation.EdgeIndices.Contains(index));
        int circle = tessellation.EdgeIndices[0];

        scene.ShowElementsIn(viewer,
        [
            new ElementReference([right, brep], ElementKind.Face, 1),
            new ElementReference([left, brep], ElementKind.Edge, circle),
            new ElementReference([left, brep], ElementKind.Edge, seam), // not shown
            new ElementReference([left, brep], ElementKind.Vertex, 1),
            new ElementReference([left, brep], ElementKind.Face, 9), // no such face
            new ElementReference([new BlockReference(block, Transform.Identity), brep], ElementKind.Face, 0),
        ]);

        Assert.Equal(
        [
            new ViewerElement(1, NativeElementKind.Face, 1),
            new ViewerElement(0, NativeElementKind.Edge, circle),
            new ViewerElement(0, NativeElementKind.Vertex, 1),
        ], viewer.HighlightedElements);

        document.Root.Entities.Remove(left);
        scene.Update();
        scene.ShowElementsIn(viewer, [new ElementReference([left, brep], ElementKind.Face, 0)]);
        Assert.Empty(viewer.HighlightedElements);
        scene.ShowElementsIn(viewer, null);
        Assert.Empty(viewer.HighlightedElements);
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
