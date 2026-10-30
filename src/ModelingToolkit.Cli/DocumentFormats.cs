using ModelingToolkit.Core.IO;

namespace ModelingToolkit.Cli;

/// <summary>Chooses the reader or the writer of a file from its extension.</summary>
internal sealed class DocumentFormats(IEnumerable<IDocumentReader> readers, IEnumerable<IDocumentWriter> writers)
{
    /// <exception cref="NotSupportedException">No reader handles the extension of the file.</exception>
    public IDocumentReader ReaderFor(string path) => Find(readers, reader => reader.Extensions, path, "read");

    /// <exception cref="NotSupportedException">No writer handles the extension of the file.</exception>
    public IDocumentWriter WriterFor(string path) => Find(writers, writer => writer.Extensions, path, "written");

    private static T Find<T>(IEnumerable<T> formats, Func<T, IReadOnlyList<string>> extensions, string path, string verb)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return formats.FirstOrDefault(format => extensions(format).Contains(extension))
            ?? throw new NotSupportedException(
                $"'{extension}' files cannot be {verb}. Supported: {string.Join(", ", formats.SelectMany(extensions).Order())}.");
    }
}
