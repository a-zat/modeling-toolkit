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
        using (var bridge = new ProgressBridge(progress, cancellationToken))
        {
            NativeProgress native = bridge.Native;
            NativeStatus status = NativeMethods.SceneReadGltf(fullPath, bridge.IsUsed ? &native : null, out scene);
            if (status == NativeStatus.KernelError)
            {
                throw new InvalidDataException(NativeMethods.LastErrorMessage());
            }
            bridge.ThrowIfFailed(status);
        }

        using (scene)
        {
            var converter = new SceneConverter(scene);
            Document document = converter.Convert();
            foreach (string warning in converter.Warnings.Select(WithoutFilePrefix).Distinct())
            {
                warnings.Add(warning);
            }
            return document;
        }
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
