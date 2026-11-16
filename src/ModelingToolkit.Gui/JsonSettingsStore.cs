using System.Text.Json;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui;

/// <summary>
/// The settings in <c>settings.json</c>, in the folder of the application data of the user (on macOS
/// <c>~/Library/Application Support/ModelingToolkit</c>), written when they change and read again when the file is
/// edited. A file that cannot be read leaves the settings as they were.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly FileSystemWatcher? _watcher;
    private string? _written; // the text last written, whose change notifications are our own

    public JsonSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModelingToolkit", "settings.json"))
    {
    }

    public JsonSettingsStore(string location)
    {
        Location = location;
        string folder = Path.GetDirectoryName(location)!;
        try
        {
            Directory.CreateDirectory(folder);
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(location))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or PlatformNotSupportedException)
        {
            _watcher = null; // the settings still work, without following the file
        }
    }

    public string? Location { get; }

    public event EventHandler? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Location))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Location!), Options) ?? new AppSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }

        AppSettings defaults = new();
        Save(defaults); // there to be edited
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        string text = JsonSerializer.Serialize(settings, Options);
        try
        {
            if (File.Exists(Location) && File.ReadAllText(Location!) == text)
            {
                return;
            }
            _written = text;
            File.WriteAllText(Location!, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Kept for this session only.
        }
    }

    public void Dispose() => _watcher?.Dispose();

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (File.ReadAllText(Location!) == _written)
            {
                return;
            }
        }
        catch (IOException)
        {
            return; // still being written: another notification follows
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
