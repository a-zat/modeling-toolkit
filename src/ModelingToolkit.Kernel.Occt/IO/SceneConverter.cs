using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>Builds a <see cref="Document"/> from the tables of a native scene, copying its data.</summary>
internal sealed unsafe class SceneConverter(SceneHandle scene)
{
    /// <summary>Messages about data the bridge could not read; filled by <see cref="Convert"/>.</summary>
    public List<string> Warnings { get; } = [];

    public Document Convert()
    {
        NativeMethods.SceneGetInfo(scene, out SceneInfo info).ThrowIfFailed();
        var document = new Document();

        for (int index = 0; index < info.TextureCount; index++)
        {
            document.Textures.Add(ReadTexture(index));
        }
        for (int index = 0; index < info.MaterialCount; index++)
        {
            document.Materials.Add(ReadMaterial(index, document.Textures));
        }

        // Every block exists before any is filled, so that references can point to all of them. Block 0 is the root.
        var blocks = new Block[info.BlockCount];
        var blockInfos = new BlockInfo[info.BlockCount];
        for (int index = 0; index < info.BlockCount; index++)
        {
            NativeMethods.SceneGetBlock(scene, index, out blockInfos[index]).ThrowIfFailed();
            if (index == 0)
            {
                blocks[index] = document.Root;
            }
            else
            {
                blocks[index] = new Block(NativeMethods.ToManagedString(blockInfos[index].Name));
                document.Blocks.Add(blocks[index]);
            }
        }
        for (int index = 0; index < info.BlockCount; index++)
        {
            BlockInfo block = blockInfos[index];
            for (int mesh = block.FirstMesh; mesh < block.FirstMesh + block.MeshCount; mesh++)
            {
                blocks[index].Entities.Add(ReadMesh(mesh, document.Materials));
            }
            for (int reference = block.FirstReference; reference < block.FirstReference + block.ReferenceCount; reference++)
            {
                blocks[index].Entities.Add(ReadReference(reference, blocks));
            }
        }

        for (int index = 0; index < info.WarningCount; index++)
        {
            NativeMethods.SceneGetWarning(scene, index, out nint message).ThrowIfFailed();
            Warnings.Add(NativeMethods.ToManagedString(message));
        }
        return document;
    }

    private Texture ReadTexture(int index)
    {
        NativeMethods.SceneGetTexture(scene, index, out TextureInfo texture).ThrowIfFailed();
        byte[] data = new ReadOnlySpan<byte>(texture.Data, checked((int)texture.Size)).ToArray();
        return new Texture(
            NativeMethods.ToManagedString(texture.Name), NativeMethods.ToManagedString(texture.MediaType), data);
    }

    private Material ReadMaterial(int index, List<Texture> textures)
    {
        NativeMethods.SceneGetMaterial(scene, index, out MaterialInfo material).ThrowIfFailed();
        return new Material(NativeMethods.ToManagedString(material.Name))
        {
            BaseColor = new Color(
                material.BaseColor[0], material.BaseColor[1], material.BaseColor[2], material.BaseColor[3]),
            Metallic = material.Metallic,
            Roughness = material.Roughness,
            BaseColorTexture = material.BaseColorTexture >= 0 ? textures[material.BaseColorTexture] : null,
        };
    }

    private MeshEntity ReadMesh(int index, List<Material> materials)
    {
        NativeMethods.SceneGetMesh(scene, index, out MeshInfo mesh).ThrowIfFailed();
        MeshEntity entity = mesh.ToMeshEntity();
        entity.Material = mesh.Material >= 0 ? materials[mesh.Material] : null;
        return entity;
    }

    private BlockReference ReadReference(int index, Block[] blocks)
    {
        NativeMethods.SceneGetReference(scene, index, out ReferenceInfo reference).ThrowIfFailed();
        double* m = reference.Transform;
        var transform = new Transform(
            m[0], m[1], m[2], m[3],
            m[4], m[5], m[6], m[7],
            m[8], m[9], m[10], m[11]);
        string name = NativeMethods.ToManagedString(reference.Name);
        return new BlockReference(blocks[reference.Block], transform) { Name = name.Length > 0 ? name : null };
    }
}
