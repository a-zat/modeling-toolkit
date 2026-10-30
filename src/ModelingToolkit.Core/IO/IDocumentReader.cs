using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.IO;

/// <summary>Reads documents from the files of one format.</summary>
public interface IDocumentReader
{
    /// <summary>Name of the format, for messages, e.g. <c>glTF</c>.</summary>
    string FormatName { get; }

    /// <summary>Lower-case extensions of the files of the format, dot included, e.g. <c>.glb</c>.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not valid in this format.</exception>
    Document Read(string path);
}
