using System.Globalization;
using System.Text;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
using ModelingToolkit.Kernel.Occt;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Gui.Tests;

/// <summary>The scene of the 3D view: meshes shared by their placements, materials by block, textures listed once.</summary>
public sealed class ViewportSceneTests
{
    [Fact]
    public void TheWheelsOfTheMilkTruckShareTheirMesh()
    {
        Document document = new GltfReader().Read(Paths.Asset("gltf", "CesiumMilkTruck.glb"), []);
        DocumentStatistics statistics = DocumentStatistics.Of(document);

        var scene = ViewportScene.Of(document);

        Assert.Equal(statistics.Meshes, scene.Meshes.Count);
        Assert.Equal(statistics.PlacedMeshes, scene.Instances.Count);
        Assert.True(scene.Instances.Count > scene.Meshes.Count);
        Assert.NotEmpty(scene.Textures);
    }

    [Fact]
    public void AShapeIsShownAsItsTessellationMadeOnceWhateverTheNumberOfPlacements()
    {
        var kernel = new OcctKernel();
        var box = new BrepEntity(kernel, kernel.MakeBox(100, 100, 100));
        var pin = new BrepEntity(kernel, kernel.MakeCylinder(5, 50));
        var pinBlock = new Block("Pin") { Entities = { pin } };
        var first = new BlockReference(pinBlock, Transform.Translation(200, 0, 0));
        var second = new BlockReference(pinBlock, Transform.Translation(300, 0, 0));
        var document = new Document();
        document.Root.Entities.Add(box);
        document.Root.Entities.Add(first);
        document.Root.Entities.Add(second);

        var scene = ViewportScene.Of(document);

        Assert.Equal(2, scene.Meshes.Count);
        Assert.All(scene.Meshes, mesh => Assert.NotEmpty(mesh!.Triangles));
        Assert.Equal([0, 1, 1], scene.Instances.Select(instance => instance.Mesh));
        Assert.Equal(
            [Transform.Identity, Transform.Translation(200, 0, 0), Transform.Translation(300, 0, 0)],
            scene.Instances.Select(instance => instance.Placement));
        Assert.All(
            scene.Instances,
            instance => Assert.Same(ViewportScene.DefaultMaterial, scene.Materials[instance.Material]));
        Assert.Equal(new Entity[] { box }, scene.PathOf(0)!.ToArray());
        Assert.Equal(new Entity[] { second, pin }, scene.PathOf(2)!.ToArray());
        Assert.Equal(2..3, scene.InstancesOf([second]));
    }

    [Fact]
    public void AShapeTakesTheDefaultMaterialAndItsPaintedFacesTheirOwn()
    {
        var kernel = new OcctKernel();
        var own = new Material("Own");
        var inner = new Block("Inner")
        {
            Entities =
            {
                new BrepEntity(kernel, kernel.MakeBox(1, 1, 1)),
                new BrepEntity(kernel, kernel.MakeBox(2, 2, 2), new Dictionary<int, Material> { [1] = own }),
            },
        };
        var document = new Document();
        document.Root.Entities.Add(new BlockReference(inner, Transform.Identity));
        var scene = ViewportScene.Of(document);
        var viewer = new RecordingViewer();

        scene.ShowIn(viewer, []);

        Assert.All(scene.Instances, instance => Assert.Same(ViewportScene.DefaultMaterial, scene.Materials[instance.Material]));
        int[] faces = Assert.Single(viewer.FaceMaterials).Value; // of the second box only
        Assert.Equal(6, faces.Length);
        Assert.Same(own, viewer.Materials[faces[1]]);
        Assert.Equal(5, faces.Count(material => material == -1));
    }

    [Fact]
    public void AShapeIsTessellatedInProportionToItsSize()
    {
        var kernel = new OcctKernel();
        var small = new Document();
        small.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeSphere(20)));
        var large = new Document();
        large.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeSphere(2000)));

        int smallTriangles = ViewportScene.Of(small).Meshes.Single()!.Triangles.Length;
        int largeTriangles = ViewportScene.Of(large).Meshes.Single()!.Triangles.Length;

        Assert.True(smallTriangles > 100);
        Assert.Equal(smallTriangles, largeTriangles);
    }

    [Fact]
    public void AShapeIsDrawnWithItsOwnEdgesAndAMeshWithItsSharpOnes()
    {
        var kernel = new OcctKernel();
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(10, 20, 30)));
        document.Root.Entities.Add(Square());

        var scene = ViewportScene.Of(document);

        Assert.Equal(2, scene.Meshes.Count);
        Assert.Equal(12, scene.MeshEdges[0]!.Count);
        Assert.Equal(4, scene.MeshEdges[1]!.Count); // the border of the square, not its diagonal
    }

    [Fact]
    public void AShapeThatCannotBeTessellatedIsLeftOutWithAWarning()
    {
        var kernel = new FailingKernel();
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(1, 1, 1)) { Name = "Broken" });
        document.Root.Entities.Add(Square());

        var scene = ViewportScene.Of(document);

        Assert.Single(scene.Instances);
        Assert.Equal([$"Broken cannot be shown: {FailingKernel.Reason}"], scene.Warnings);
    }

    [Fact]
    public void AShapeWithoutGeometryIsLeftOutWithAWarning()
    {
        var kernel = new OcctKernel();
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.Deserialize(Encoding.ASCII.GetBytes(
            "CASCADE Topology V3, (c) Open Cascade\nLocations 0\nCurve2ds 0\nCurves 0\nPolygon3D 0\n" +
            "PolygonOnTriangulations 0\nSurfaces 0\nTriangulations 0\n\nTShapes 1\nCo\n\n1100000\n*\n\n+1 0\n")))
        {
            Name = "Empty",
        });

        var scene = ViewportScene.Of(document);

        Assert.Empty(scene.Instances);
        Assert.Equal(["Empty cannot be shown: it has no geometry."], scene.Warnings);
        Assert.True(document.GetBoundingBox().IsEmpty);
    }

    [Fact]
    public void ManyShapesAreTessellatedOnceEachWithTheWarningsInTheOrderOfTheModel()
    {
        var kernel = new OcctKernel();
        var failing = new FailingKernel();
        var shared = new BrepEntity(kernel, kernel.MakeSphere(10));
        var block = new Block("Shared") { Entities = { shared } };
        var document = new Document();
        document.Blocks.Add(block);
        for (int index = 0; index < 20; index++)
        {
            document.Root.Entities.Add(index % 5 == 0
                ? new BrepEntity(failing, failing.MakeBox(1, 1, 1)) { Name = $"Broken {index}" }
                : new BrepEntity(kernel, kernel.MakeCylinder(5 + index, 10)));
        }
        document.Root.Entities.Add(new BlockReference(block, Transform.Identity));
        document.Root.Entities.Add(new BlockReference(block, Transform.Translation(100, 0, 0)));

        var scene = ViewportScene.Of(document);

        Assert.Equal(16 + 1, scene.Meshes.Count); // the sphere once, for its two placements
        Assert.Equal(16 + 2, scene.Instances.Count);
        Assert.Equal(
            [0, 5, 10, 15],
            scene.Warnings.Select(warning => int.Parse(warning.Split(' ')[1], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void PlacementsComposeTheTransformsOfNestedReferences()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var axle = new Block("Axle")
        {
            Entities =
            {
                new BlockReference(wheel, Transform.Translation(-500, 0, 0)),
                new BlockReference(wheel, Transform.Translation(500, 0, 0)),
            },
        };
        var document = new Document();
        document.Root.Entities.Add(new BlockReference(axle, Transform.Translation(0, 2000, 300)));

        var scene = ViewportScene.Of(document);

        Assert.Single(scene.Meshes);
        Assert.Equal(
            [Transform.Translation(-500, 2000, 300), Transform.Translation(500, 2000, 300)],
            scene.Instances.Select(instance => instance.Placement));
    }

    [Fact]
    public void MeshesWithoutMaterialTakeTheDefaultOneWhateverTheirReferences()
    {
        var own = new Material("Own");
        var inner = new Block("Inner") { Entities = { Square(), Square(own) } };
        var outer = new Block("Outer")
        {
            Entities = { new BlockReference(inner, Transform.Identity), new BlockReference(inner, Transform.Identity) },
        };
        var document = new Document();
        document.Root.Entities.Add(new BlockReference(outer, Transform.Identity));
        document.Root.Entities.Add(Square());

        var scene = ViewportScene.Of(document);

        Material none = ViewportScene.DefaultMaterial;
        Assert.Equal([none, own, none, own, none], scene.Instances.Select(instance => scene.Materials[instance.Material]));
    }

    [Fact]
    public void MaterialsAndTexturesAreListedOnce()
    {
        var texture = new Texture("Checker", "image/png", [1, 2, 3]);
        var first = new Material("First") { BaseColorTexture = texture };
        var second = new Material("Second") { BaseColorTexture = texture };
        var document = new Document();
        document.Root.Entities.Add(Square(first));
        document.Root.Entities.Add(Square(first));
        document.Root.Entities.Add(Square(second));
        document.Root.Entities.Add(Square(new Material("Plain")));

        var scene = ViewportScene.Of(document);

        Assert.Equal([first, second, scene.Materials[2]], scene.Materials);
        Assert.Equal([texture], scene.Textures);
        Assert.Equal([0, 0, -1], scene.MaterialTextures);
    }

    [Fact]
    public void TheInstancesOfANodeOfTheTreeAreThoseItPlaces()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var left = new BlockReference(wheel, Transform.Translation(-500, 0, 0));
        var right = new BlockReference(wheel, Transform.Translation(500, 0, 0));
        var axle = new Block("Axle") { Entities = { new MeshEntity([new Point3d(0, 0, 0)], []), left, right } };
        var front = new BlockReference(axle, Transform.Translation(0, 2000, 0));
        var rear = new BlockReference(axle, Transform.Identity);
        MeshEntity body = Square();
        var document = new Document();
        document.Root.Entities.Add(front);
        document.Root.Entities.Add(rear);
        document.Root.Entities.Add(body);

        var scene = ViewportScene.Of(document);

        Assert.Equal(0..5, scene.InstancesOf([]));
        Assert.Equal(0..2, scene.InstancesOf([front]));
        Assert.Equal(3..4, scene.InstancesOf([rear, right]));
        Assert.Equal(3..4, scene.InstancesOf([rear, right, wheel.Entities[0]]));
        Assert.Equal(Transform.Translation(500, 0, 0), scene.Instances[3].Placement);
        Assert.Equal(4..5, scene.InstancesOf([body]));
        Assert.Equal(0, scene.InstancesOf([rear, axle.Entities[0]]).GetOffsetAndLength(5).Length);
    }

    [Fact]
    public void ThePathOfAnInstanceLeadsToItsMesh()
    {
        var wheel = new Block("Wheel") { Entities = { Square() } };
        var left = new BlockReference(wheel, Transform.Translation(-500, 0, 0));
        var right = new BlockReference(wheel, Transform.Translation(500, 0, 0));
        var axle = new Block("Axle") { Entities = { new MeshEntity([new Point3d(0, 0, 0)], []), left, right } };
        var front = new BlockReference(axle, Transform.Translation(0, 2000, 0));
        var rear = new BlockReference(axle, Transform.Identity);
        MeshEntity body = Square();
        var document = new Document();
        document.Root.Entities.Add(front);
        document.Root.Entities.Add(rear);
        document.Root.Entities.Add(body);

        var scene = ViewportScene.Of(document);

        Assert.Equal([rear, right, wheel.Entities[0]], scene.PathOf(3));
        Assert.Equal([body], scene.PathOf(4));
        Assert.All(Enumerable.Range(0, scene.Instances.Count),
            instance => Assert.Equal(instance..(instance + 1), scene.InstancesOf(scene.PathOf(instance)!)));
        Assert.Null(scene.PathOf(-1));
        Assert.Null(scene.PathOf(scene.Instances.Count));
        Assert.Null(scene.PathOfShown(0)); // not shown in a viewer
    }

    [Fact]
    public void PathsThatAreNotInTheDocumentPlaceNothing()
    {
        MeshEntity square = Square();
        var block = new Block("Block") { Entities = { square } };
        var reference = new BlockReference(block, Transform.Identity);
        var document = new Document();
        document.Root.Entities.Add(reference);

        var scene = ViewportScene.Of(document);

        Assert.Equal(default, scene.InstancesOf([square]));
        Assert.Equal(default, scene.InstancesOf([reference, square, square]));
        Assert.Equal(default, scene.InstancesOf([new BlockReference(block, Transform.Identity)]));
    }

    [Fact]
    public void MeshesWithoutTrianglesAreLeftOut()
    {
        var document = new Document();
        document.Root.Entities.Add(new MeshEntity([new Point3d(0, 0, 0)], []));

        var scene = ViewportScene.Of(document);

        Assert.Empty(scene.Meshes);
        Assert.Empty(scene.Instances);
    }

    private static MeshEntity Square(Material? material = null) => new(
        [new Point3d(0, 0, 0), new Point3d(1000, 0, 0), new Point3d(1000, 1000, 0), new Point3d(0, 1000, 0)],
        [new Triangle(0, 1, 2), new Triangle(0, 2, 3)])
    {
        Material = material,
    };
}
