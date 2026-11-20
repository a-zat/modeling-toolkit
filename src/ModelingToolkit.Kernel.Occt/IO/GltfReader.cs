using System.Text.Json;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Reads glTF files (<c>.gltf</c>, <c>.glb</c>) with OpenCASCADE, converting them to millimetres and Z up.
/// Meshes used by several nodes become blocks, referenced by each node.
/// </summary>
public sealed class GltfReader : IDocumentReader
{
    public string FormatName => "glTF";

    public IReadOnlyList<string> Extensions { get; } = [".gltf", ".glb"];

    public unsafe Document Read(string path, ICollection<string> warnings, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(warnings);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.", fullPath);
        }

        GltfExtensions.Check(path, warnings);

        SceneHandle scene;
        string? raised = GltfScales.RaisedCopy(fullPath);
        try
        {
            using var bridge = new ProgressBridge(progress, cancellationToken);
            NativeProgress native = bridge.Native;
            NativeStatus status = NativeMethods.SceneReadGltf(raised ?? fullPath, bridge.IsUsed ? &native : null, out scene);
            if (status == NativeStatus.KernelError)
            {
                throw new InvalidDataException(NativeMethods.LastErrorMessage());
            }
            bridge.ThrowIfFailed(status);
        }
        finally
        {
            if (raised is not null)
            {
                File.Delete(raised);
            }
        }

        using (scene)
        {
            var converter = new SceneConverter(scene);
            Document document = converter.Convert();
            foreach (string warning in converter.Warnings.Select(WithoutFilePrefix).Distinct())
            {
                warnings.Add(warning);
            }
            DropAxisNode(document);
            ReadSceneName(fullPath, document);
            ReadAnimations(fullPath, document, warnings);
            ReadExtras(fullPath, document);
            ReadAnchors(fullPath, document, warnings);
            return document;
        }
    }

    // OpenCASCADE puts the nodes of the scene under one of its own, "Yup2Zup", which turns the axes of glTF into its
    // own; the file has no such node, so its entities, turned as it turns them, are those of the model space.
    private static void DropAxisNode(Document document)
    {
        if (document.Root.Entities is [BlockReference { Name: "Yup2Zup", IsHidden: false } wrapper] &&
            wrapper.Block != document.Root &&
            document.Blocks.SelectMany(block => block.Entities).OfType<BlockReference>()
                .All(reference => reference == wrapper || reference.Block != wrapper.Block))
        {
            Transform turn = wrapper.Transform;
            Block block = wrapper.Block;
            document.Root.Entities.Remove(wrapper);
            while (block.Entities.Count > 0)
            {
                Entity entity = block.Entities[0];
                block.Entities.RemoveAt(0);
                if (entity is BlockReference reference)
                {
                    reference.Transform = turn * reference.Transform;
                    document.Root.Entities.Add(reference);
                }
                else
                {
                    document.Root.Entities.Add(Core.Editing.TransformCommands.Duplicate(entity, turn));
                }
            }
            document.Blocks.Remove(block);
        }
    }

    // The name of the scene, when it has one, is the name of the model.
    private static void ReadSceneName(string path, Document document)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(GltfAnimations.Load(path).Json);
            JsonElement root = file.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("scenes", out JsonElement scenes) &&
                scenes.ValueKind == JsonValueKind.Array && scenes.GetArrayLength() > 0)
            {
                int index = root.TryGetProperty("scene", out JsonElement chosen) && chosen.TryGetInt32(out int number) &&
                    number >= 0 && number < scenes.GetArrayLength() ? number : 0;
                JsonElement scene = scenes[index];
                if (scene.ValueKind == JsonValueKind.Object && scene.TryGetProperty("name", out JsonElement name) &&
                    name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } text)
                {
                    document.Root.Name = text;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException)
        {
        }
    }

    // The geometry is read already: animations that cannot be read leave it as it is.
    private static void ReadAnimations(string path, Document document, ICollection<string> warnings)
    {
        var found = new List<string>();
        try
        {
            document.Animations.AddRange(GltfAnimations.Read(path, document, found));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException or ArgumentException)
        {
            found.Add($"The animations cannot be read: {exception.Message}");
        }
        foreach (string warning in found.Distinct())
        {
            warnings.Add(warning);
        }
    }

    // What the toolkit writes in the extras of glTF, which OpenCASCADE ignores: the hidden nodes, found by name as the
    // animated ones, and the size of the textures of materials; and the metalness of materials with a texture of it. Extras that cannot be read are of no harm.
    private static void ReadExtras(string path, Document document)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(GltfAnimations.Load(path).Json);
            JsonElement root = file.RootElement;
            var entities = document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities)
                .Where(entity => entity.Name is not null).ToLookup(entity => entity.Name!);
            foreach (JsonElement node in Items(root, "nodes"))
            {
                if (!node.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String ||
                    entities[name.GetString()!].ToList() is not [Entity entity])
                {
                    continue;
                }
                if (Extra(node, "hidden") is { ValueKind: JsonValueKind.True })
                {
                    entity.IsHidden = true;
                }
                if (Extra(node, "group") is { ValueKind: JsonValueKind.True } && entity is BlockReference group)
                {
                    group.IsGroup = true;
                }
            }
            var materials = document.Materials.ToLookup(material => material.Name);
            foreach (JsonElement entry in Items(root, "materials"))
            {
                TextureMetalness(entry, materials);
                if (Extra(entry, "textureSize") is { ValueKind: JsonValueKind.Number } size &&
                    entry.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                    size.GetDouble() > 0)
                {
                    foreach (Material material in materials[name.GetString()!])
                    {
                        material.TextureSize = (float)size.GetDouble();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException)
        {
        }

        // glTF makes the metallic factor 1 when a file leaves it out, and with a texture of metalness and roughness the
        // factors only scale it: the material would be all metal, white and shadowless, where the files that leave it
        // out are of materials that are not. So it is 0 then; a factor that the file states is kept, unless a texture
        // of it comes with it, which is not read.
        static void TextureMetalness(JsonElement entry, ILookup<string, Material> materials)
        {
            if (!entry.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                return;
            }
            bool stated = entry.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) &&
                pbr.ValueKind == JsonValueKind.Object;
            if (!stated || !pbr.TryGetProperty("metallicFactor", out _) || pbr.TryGetProperty("metallicRoughnessTexture", out _))
            {
                foreach (Material material in materials[name.GetString()!])
                {
                    material.Metallic = 0;
                }
            }
        }

        static IEnumerable<JsonElement> Items(JsonElement root, string property) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement array) &&
            array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

        static JsonElement? Extra(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty("extras", out JsonElement extras) &&
            extras.ValueKind == JsonValueKind.Object && extras.TryGetProperty(name, out JsonElement value)
                ? value : null;
    }

    // The anchors that the toolkit writes as empty nodes, which OpenCASCADE leaves out, each with its kind in its extras,
    // put in the block of the reference of the node above it; and the attachments, in the extras of the nodes that
    // follow them. References are found by the names of the nodes from the top down, and by their order among nodes of
    // the same name: an anchor or an attachment whose reference cannot be told apart so is left out, with a warning.
    private static void ReadAnchors(string path, Document document, ICollection<string> warnings)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(GltfAnimations.Load(path).Json);
            if (!file.RootElement.TryGetProperty("nodes", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            JsonElement[] nodes = [.. array.EnumerateArray()];
            var parents = new Dictionary<int, int>();
            for (int index = 0; index < nodes.Length; index++)
            {
                if (nodes[index].TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement child in children.EnumerateArray())
                    {
                        if (child.TryGetInt32(out int c))
                        {
                            parents[c] = index;
                        }
                    }
                }
            }
            var references = document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities)
                .OfType<BlockReference>().Where(reference => reference.Name is not null).ToLookup(reference => reference.Name!);
            int[] roots = [.. Enumerable.Range(0, nodes.Length).Where(index => !parents.ContainsKey(index))];
            var found = new Dictionary<int, BlockReference?>();
            // The reference of a node: down the nodes above it, each the one of its name among the references of the
            // block of the one above, in the order of the nodes of that name, when there are as many of each; else the
            // one reference of that name in the model.
            BlockReference? ReferenceOf(int node)
            {
                if (found.TryGetValue(node, out BlockReference? known))
                {
                    return known;
                }
                found[node] = null; // against a cycle of nodes
                BlockReference? reference = null;
                if (Name(nodes[node]) is { } name)
                {
                    Block? block = parents.TryGetValue(node, out int parent) ? ReferenceOf(parent)?.Block : document.Root;
                    if (block is not null)
                    {
                        BlockReference[] same = [.. block.Entities.OfType<BlockReference>()
                            .Where(candidate => (candidate.Name ?? candidate.Block.Name) == name)];
                        int[] siblings = parents.TryGetValue(node, out int above)
                            ? [.. ChildrenOf(above).Where(child => Name(nodes[child]) == name)]
                            : [.. roots.Where(root => Name(nodes[root]) == name)];
                        if (same.Length == siblings.Length && System.Array.IndexOf(siblings, node) is >= 0 and int k)
                        {
                            reference = same[k];
                        }
                    }
                    if (reference is null && references[name].ToList() is [BlockReference only])
                    {
                        reference = only;
                    }
                }
                return found[node] = reference;
            }
            IEnumerable<int> ChildrenOf(int node) =>
                nodes[node].TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array
                    ? children.EnumerateArray().Where(child => child.TryGetInt32(out _)).Select(child => child.GetInt32())
                    : [];

            var anchors = new Dictionary<int, AnchorEntity>();
            int lost = 0;
            for (int index = 0; index < nodes.Length; index++)
            {
                if (Extra(nodes[index], "anchor") is not { ValueKind: JsonValueKind.String } kind)
                {
                    continue;
                }
                if (!parents.TryGetValue(index, out int parent) || ReferenceOf(parent) is not { } holder)
                {
                    lost++;
                    continue;
                }
                var anchor = new AnchorEntity(FromGltf(Matrix(nodes[index])),
                    kind.GetString() == "pointOfView" ? AnchorKind.PointOfView : AnchorKind.Attach) { Name = Name(nodes[index]) };
                holder.Block.Entities.Add(anchor);
                anchors[index] = anchor;
            }
            for (int index = 0; index < nodes.Length; index++)
            {
                if (Extra(nodes[index], "follows") is not { ValueKind: JsonValueKind.Number } follows ||
                    !follows.TryGetInt32(out int anchorNode))
                {
                    continue;
                }
                // A node of the scene has no node above it: the chain is then from the top of the scene.
                int top = parents.GetValueOrDefault(index, -1);
                if (ReferenceOf(index) is not { } follower || !anchors.TryGetValue(anchorNode, out AnchorEntity? anchor) ||
                    Chain(anchorNode, top) is not { } chainNodes ||
                    chainNodes.Select(ReferenceOf).ToList() is not { } chain || chain.Contains(null))
                {
                    lost++;
                    continue;
                }
                try
                {
                    BlockReference[] links = [.. chain.Select(reference => reference!)];
                    document.Attach(new Attachment(follower, links, anchor, Attachment.OffsetOf(follower, links, anchor)));
                }
                catch (ArgumentException)
                {
                    lost++;
                }
            }
            if (lost > 0)
            {
                warnings.Add($"{lost} anchor(s) or attachment(s) are left out: the components they belong to cannot be told apart by their names.");
            }

            // The nodes from below the one given down to the one holding the anchor; null if it is not under it.
            List<int>? Chain(int anchorNode, int top)
            {
                var chain = new List<int>();
                for (int node = parents.GetValueOrDefault(anchorNode, -1); node != top; node = parents.GetValueOrDefault(node, -1))
                {
                    if (node < 0)
                    {
                        return null;
                    }
                    chain.Insert(0, node);
                }
                return chain;
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException)
        {
        }

        static string? Name(JsonElement node) =>
            node.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;

        static JsonElement? Extra(JsonElement element, string name) =>
            element.TryGetProperty("extras", out JsonElement extras) && extras.ValueKind == JsonValueKind.Object &&
            extras.TryGetProperty(name, out JsonElement value) ? value : null;

        // The matrix of a node, by columns, or its translation, rotation and scale; the identity for none.
        static Transform Matrix(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out JsonElement matrix) && matrix.ValueKind == JsonValueKind.Array &&
                matrix.GetArrayLength() == 16)
            {
                double[] m = [.. matrix.EnumerateArray().Select(value => value.GetDouble())];
                return new Transform(m[0], m[4], m[8], m[12], m[1], m[5], m[9], m[13], m[2], m[6], m[10], m[14]);
            }
            return Transform.Identity;
        }
    }

    // A transformation of glTF in the toolkit: conjugated by the rotation of the axes, in millimetres.
    private static Transform FromGltf(Transform t)
    {
        static Vector3d From(Vector3d v) => new(v.X, -v.Z, v.Y);
        static Vector3d To(Vector3d v) => new(v.X, v.Z, -v.Y);
        Vector3d[] columns = [.. new[] { Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ }
            .Select(axis => From(t.ApplyVector(To(axis))))];
        Vector3d translation = From(new Vector3d(t.M03, t.M13, t.M23)) * 1000;
        return new Transform(
            columns[0].X, columns[1].X, columns[2].X, translation.X,
            columns[0].Y, columns[1].Y, columns[2].Y, translation.Y,
            columns[0].Z, columns[1].Z, columns[2].Z, translation.Z);
    }

    // OpenCASCADE starts each problem of the file with "File '<path>' defines invalid glTF!" on a line of its own.
    private static string WithoutFilePrefix(string warning)
    {
        const string marker = " defines invalid glTF!\n";
        int index = warning.IndexOf(marker, StringComparison.Ordinal);
        return warning.StartsWith("File '", StringComparison.Ordinal) && index >= 0
            ? warning[(index + marker.Length)..]
            : warning;
    }
}
