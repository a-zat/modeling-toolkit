using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.IO;

/// <summary>Writes documents to the files of one format.</summary>
public interface IDocumentWriter
{
    /// <summary>Name of the format, for messages.</summary>
    string FormatName { get; }

    /// <summary>Lower-case extensions of the files of the format, dot included.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Writes <paramref name="document"/>, replacing the file if it exists. A failed or cancelled writing leaves the
    /// file as it was.
    /// </summary>
    /// <param name="progress">Receives the part of the document written, from 0 to 1.</param>
    /// <param name="cancellationToken">Stops the writing.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    void Write(Document document, string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
