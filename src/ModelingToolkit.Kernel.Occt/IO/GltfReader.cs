using Microsoft.Extensions.Logging;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Reads glTF files (<c>.gltf</c>, <c>.glb</c>) with OpenCASCADE, converting them to millimetres and Z up.
/// Meshes used by several nodes become blocks, referenced by each node.
/// </summary>
public sealed class GltfReader(ILogger<GltfReader> logger) : IDocumentReader
{
    public string FormatName => "glTF";

    public IReadOnlyList<string> Extensions { get; } = [".gltf", ".glb"];

    public Document Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.", fullPath);
        }

        NativeStatus status = NativeMethods.SceneReadGltf(fullPath, out SceneHandle scene);
        if (status == NativeStatus.KernelError)
        {
            throw new InvalidDataException(NativeMethods.LastErrorMessage());
        }
        status.ThrowIfFailed();

        using (scene)
        {
            var converter = new SceneConverter(scene);
            Document document = converter.Convert();
            foreach (string warning in converter.Warnings)
            {
                logger.LogWarning("{Path}: {Warning}", path, warning);
            }
            return document;
        }
    }
}
