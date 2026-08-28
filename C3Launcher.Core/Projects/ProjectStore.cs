using System.Text.Json;

namespace C3Launcher.Core.Projects;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public ProjectStore(string? path = null) => _path = path ?? DefaultPath;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "C3Launcher",
        "settings.json");

    public LauncherSettings Load()
    {
        if (!File.Exists(_path))
            return new LauncherSettings();

        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_path), SerializerOptions)
                ?? new LauncherSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temp, _path, overwrite: true);
    }

    public static ProjectEntry CreateEntry(string projectPath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        var name = Path.GetFileName(full);

        return new ProjectEntry
        {
            Path = full,
            Name = string.IsNullOrEmpty(name) ? full : name,
        };
    }

    public static void RecordLaunch(ProjectEntry entry)
    {
        entry.LastOpenedUtc = DateTime.UtcNow;
        entry.LaunchCount++;
    }
}
