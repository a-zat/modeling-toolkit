using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui.Views;

/// <summary>The file dialogs of the operating system, through the storage provider of a window.</summary>
internal sealed class StorageProviderFileDialogs(TopLevel topLevel) : IFileDialogs
{
    public async Task<string?> OpenAsync(string title, IReadOnlyList<FileType> types)
    {
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [.. types.Select(ToFilter)],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveAsync(string title, FileType type, string suggestedName)
    {
        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = type.Extensions[0].TrimStart('.'),
            FileTypeChoices = [ToFilter(type)],
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType ToFilter(FileType type) => new(type.Name)
    {
        Patterns = [.. type.Extensions.Select(extension => "*" + extension)],
    };
}
