using System.Globalization;

namespace ModelingToolkit.Gui;

/// <summary>
/// The errors that nothing handled, written with their stack to <c>errors.log</c>, in the folder of the logs of the user
/// (on macOS <c>~/Library/Logs/ModelingToolkit</c>), to find what went wrong after the fact.
/// </summary>
internal static class ErrorLog
{
    public static string Path { get; } = System.IO.Path.Combine(
        OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs")
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModelingToolkit", "errors.log");

    /// <summary>Appends an error, with the time; a failure to write is ignored, as there is nowhere else to tell it.</summary>
    public static void Write(Exception exception, string where)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}"));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}
