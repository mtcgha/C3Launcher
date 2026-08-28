namespace C3Launcher.Core.Compose;

public sealed record BindMount(string Source, string Target, bool ReadOnly);

public sealed class ComposeSpec
{
    public string ServiceName { get; init; } = "claude";
    public required string Image { get; init; }
    public bool StdinOpen { get; init; } = true;
    public bool Tty { get; init; } = true;
    public bool ReadOnlyRootFilesystem { get; init; } = true;
    public IReadOnlyList<string> CapDrop { get; init; } = ["ALL"];
    public IReadOnlyList<string> SecurityOpt { get; init; } = ["no-new-privileges"];
    public IReadOnlyList<string> Tmpfs { get; init; } = [];
    public IReadOnlyList<BindMount> Volumes { get; init; } = [];
    public IReadOnlyList<string> Command { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
}
