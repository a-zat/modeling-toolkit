using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.IO;

/// <summary>Writes documents to the files of one format.</summary>
public interface IDocumentWriter
{
    /// <summary>Name of the format, for messages.</summary>
    string FormatName { get; }

    /// <summary>Lower-case extensions of the files of the format, dot included.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Writes <paramref name="document"/>, replacing the file if it exists.</summary>
    void Write(Document document, string path);
}
