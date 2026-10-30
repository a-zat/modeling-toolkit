namespace ModelingToolkit.Gui.Tests;

internal static class Paths
{
    /// <summary>The folder of the repository, found from the test binaries.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>A file of tests/assets, copied next to the test binaries.</summary>
    public static string Asset(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "assets", .. parts]);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ModelingToolkit.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The repository was not found above the test binaries.");
    }
}
