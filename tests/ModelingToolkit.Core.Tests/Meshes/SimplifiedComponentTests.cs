using System.Numerics;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Meshes;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Tests.Meshes;

public sealed class SimplifiedComponentTests
{
    [Fact]
    public void The_meshes_of_a_component_are_simplified_as_one_change_that_can_be_undone()
    {
        var document = new Document();
        var block = new Block("Part");
        MeshEntity first = MeshSimplifierTests.Grid(10), second = MeshSimplifierTests.Grid(10, z: 100);
        block.Entities.Add(first);
        block.Entities.Add(second);
        document.Blocks.Add(block);
        var reference = new BlockReference(block, Transform.Translation(1000, 0, 0));
        document.Root.Entities.Add(reference);
        var session = new EditSession(document);

        SimplifiedComponent result = SimplifiedComponent.Of(reference, new SimplifyOptions { KeepPercent = 10, MergeByMaterial = true });
        Assert.Equal(400, result.Before.Triangles);
        Assert.True(result.After.Triangles < 200);
        // Shown in the coordinates of the component.
        Assert.All(result.Placed(reference, simplified: true), placed => Assert.Equal(Transform.Identity, placed.Placement));
        session.Execute(result.Command());

        MeshEntity merged = Assert.Single(block.Entities.OfType<MeshEntity>());
        Assert.Equal(result.After.Triangles, merged.Triangles.Length);
        session.Undo();
        Assert.Equal([first, second], block.Entities);
    }

    [Fact]
    public void With_their_normals_the_edges_of_a_smooth_surface_are_not_drawn_however_rough()
    {
        // Two triangles folded at 60°, shaded as one smooth surface by their normals, or not.
        Point3d[] vertices = [new(0, 0, 0), new(100, 0, 0), new(50, 80, 0), new(50, -40, 70)];
        Triangle[] triangles = [new(0, 1, 2), new(1, 0, 3)];
        Vector3[] smooth = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];

        Assert.Equal(5, SharpEdges.Of(vertices, triangles).Count);
        Assert.Equal(4, SharpEdges.Of(vertices, triangles, smooth).Count);
    }
}
