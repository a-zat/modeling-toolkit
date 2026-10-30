namespace ModelingToolkit.Core.IO;

/// <summary>The file formats that can be read and written; chooses the reader or the writer of a file from its extension.</summary>
public sealed class DocumentFormats(IEnumerable<IDocumentReader> readers, IEnumerable<IDocumentWriter> writers)
{
    public IReadOnlyList<IDocumentReader> Readers { get; } = [.. readers];

    public IReadOnlyList<IDocumentWriter> Writers { get; } = [.. writers];

    /// <exception cref="NotSupportedException">No reader handles the extension of the file.</exception>
    public IDocumentReader ReaderFor(string path) => Find(Readers, reader => reader.Extensions, path, "read");

    /// <exception cref="NotSupportedException">No writer handles the extension of the file.</exception>
    public IDocumentWriter WriterFor(string path) => Find(Writers, writer => writer.Extensions, path, "written");

    private static T Find<T>(IReadOnlyList<T> formats, Func<T, IReadOnlyList<string>> extensions, string path, string verb)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return formats.FirstOrDefault(format => extensions(format).Contains(extension))
            ?? throw new NotSupportedException(
                $"'{extension}' files cannot be {verb}. Supported: {string.Join(", ", formats.SelectMany(extensions).Order())}.");
    }
}
