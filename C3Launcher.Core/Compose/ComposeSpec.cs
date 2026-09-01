namespace C3Launcher.Core.Compose;

public sealed record BindMount(string Source, string Target, bool ReadOnly);

/// <summary>
/// A Docker-managed named volume. Declared external in the compose file so its
/// lifetime is C3Launcher's rather than the session's: it holds the container
/// fleet's Claude login, and 'compose down -v' would otherwise take it.
/// </summary>
public sealed record NamedVolume(string Name, string Target);

/// <summary>
/// Names and labels the project's default network. Compose would otherwise invent
/// "&lt;compose file's directory&gt;_default" and leave it behind on exit, with nothing
/// on it to prove the network was ours to delete.
/// </summary>
public sealed record ComposeNetwork(string Name, IReadOnlyDictionary<string, string> Labels);

public sealed class ComposeSpec
{
    public string ServiceName { get; init; } = "claude";
    public required string Image { get; init; }

    /// <summary>Overrides the image's WORKDIR; the project is mounted per session.</summary>
    public string? WorkingDirectory { get; init; }
    public bool StdinOpen { get; init; } = true;
    public bool Tty { get; init; } = true;
    public bool ReadOnlyRootFilesystem { get; init; } = true;
    public IReadOnlyList<string> CapDrop { get; init; } = ["ALL"];
    public IReadOnlyList<string> SecurityOpt { get; init; } = ["no-new-privileges"];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Tmpfs { get; init; } = [];
    public IReadOnlyList<BindMount> Volumes { get; init; } = [];
    public IReadOnlyList<NamedVolume> NamedVolumes { get; init; } = [];
    public IReadOnlyList<string> Command { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public ComposeNetwork? Network { get; init; }
}
