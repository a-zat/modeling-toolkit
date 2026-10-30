namespace ModelingToolkit.App.ViewModels;

/// <summary>A file format offered by the file dialogs.</summary>
/// <param name="Extensions">Lower-case extensions, dot included.</param>
public sealed record FileType(string Name, IReadOnlyList<string> Extensions);

/// <summary>The open and save dialogs of the operating system, behind an interface so that view models stay testable.</summary>
public interface IFileDialogs
{
    /// <returns>The chosen file, or null if the dialog was cancelled.</returns>
    Task<string?> OpenAsync(string title, IReadOnlyList<FileType> types);

    /// <returns>The chosen file, or null if the dialog was cancelled.</returns>
    Task<string?> SaveAsync(string title, FileType type, string suggestedName);
}
