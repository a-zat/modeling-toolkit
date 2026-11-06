using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.IO;

/// <summary>Reads documents from the files of one format.</summary>
public interface IDocumentReader
{
    /// <summary>Name of the format, for messages, e.g. <c>glTF</c>.</summary>
    string FormatName { get; }

    /// <summary>Lower-case extensions of the files of the format, dot included, e.g. <c>.glb</c>.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <param name="path">The file to read.</param>
    /// <param name="warnings">Receives a message for each part of the file that cannot be read and is left out.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not valid, or needs what is not supported.</exception>
    Document Read(string path, ICollection<string> warnings);
}
