using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;
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
    public void MeshesWithoutMaterialTakeTheOneOfTheClosestReferenceThatHasOne()
    {
        var red = new Material("Red");
        var blue = new Material("Blue");
        var own = new Material("Own");
        var inner = new Block("Inner") { Entities = { Square(), Square(own) } };
        var outer = new Block("Outer")
        {
            Entities = { new BlockReference(inner, Transform.Identity), new BlockReference(inner, Transform.Identity) { Material = blue } },
        };
        var document = new Document();
        document.Root.Entities.Add(new BlockReference(outer, Transform.Identity) { Material = red });
        document.Root.Entities.Add(Square());

        var scene = ViewportScene.Of(document);

        Assert.Equal(
            [red, own, blue, own, ViewportScene.DefaultMaterial],
            scene.Instances.Select(instance => scene.Materials[instance.Material]));
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
