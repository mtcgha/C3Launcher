using IODirectory = System.IO.Directory;

namespace C3Launcher.Core.Launching;

/// <summary>
/// The temp files one session needs, grouped under a folder named for its session
/// id. That id is also the container's label, so leftovers from a crashed or
/// force-closed launcher can be swept on the next start instead of leaking.
/// </summary>
public sealed class SessionWorkspace : IDisposable
{
    private SessionWorkspace(string sessionId, string directory)
    {
        SessionId = sessionId;
        Directory = directory;
        StubFilePath = Path.Combine(directory, "stub");
        StubDirectoryPath = Path.Combine(directory, "empty");
        ComposeFilePath = Path.Combine(directory, "compose.yml");
    }

    /// <summary>
    /// Under LocalApplicationData rather than the temp directory: these are bind-mount
    /// sources for a live container, and Storage Sense prunes %TEMP%.
    /// </summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "C3Launcher",
        "sessions");

    public string SessionId { get; }
    public string Directory { get; }
    public string StubFilePath { get; }
    public string StubDirectoryPath { get; }
    public string ComposeFilePath { get; }

    public static SessionWorkspace Create(string sessionId)
    {
        var directory = Path.Combine(Root, sessionId);
        IODirectory.CreateDirectory(directory);

        var workspace = new SessionWorkspace(sessionId, directory);
        IODirectory.CreateDirectory(workspace.StubDirectoryPath);
        File.WriteAllBytes(workspace.StubFilePath, []);

        return workspace;
    }

    /// <summary>Deletes the leftovers of every session not in <paramref name="liveSessionIds"/>.</summary>
    public static int Sweep(IEnumerable<string> liveSessionIds)
    {
        if (!IODirectory.Exists(Root))
            return 0;

        var live = liveSessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var swept = 0;

        foreach (var directory in IODirectory.EnumerateDirectories(Root))
        {
            if (live.Contains(Path.GetFileName(directory)))
                continue;

            if (TryDelete(directory))
                swept++;
        }

        return swept;
    }

    public void Dispose() => TryDelete(Directory);

    private static bool TryDelete(string directory)
    {
        try
        {
            IODirectory.Delete(directory, recursive: true);
            return true;
        }
        catch (IOException)
        {
            // Still mounted by a container we don't know about; the next sweep gets it.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
