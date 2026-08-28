using C3Launcher.Core.Auth;
using C3Launcher.Core.Launching;
using C3Launcher.Core.Mounting;

namespace C3Launcher.Core.Compose;

public sealed class ComposePlanRequest
{
    public required string ProjectRoot { get; init; }
    public required ClaudeAuth Auth { get; init; }
    public string Image { get; init; } = ContainerAssets.ImageName;
    public MountScanResult? Scan { get; init; }
    public SessionWorkspace? Workspace { get; init; }
    public IReadOnlyList<string> ClaudeArgs { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
}

public static class ComposePlanner
{
    public const string WorkspaceTarget = "/workspace";
    public const string ClaudeDirectoryTarget = "/mnt/claude-dir";
    public const string ClaudeJsonTarget = "/mnt/claude-json";

    public static readonly IReadOnlyList<string> DefaultTmpfs =
    [
        "/tmp:rw,noexec,nosuid,size=256m",
        "/home/node:rw,size=512m,uid=1000,gid=1000",
    ];

    public static ComposeSpec Build(ComposePlanRequest request)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ProjectRoot));

        var volumes = new List<BindMount>
        {
            new(request.Auth.ClaudeDirectory, ClaudeDirectoryTarget, ReadOnly: true),
            new(request.Auth.ClaudeJsonPath, ClaudeJsonTarget, ReadOnly: true),
            new(root, WorkspaceTarget, ReadOnly: false),
        };

        // More-specific targets added after /workspace take precedence, so each
        // stub hides the real path from inside the container.
        if (request.Scan is { } scan && request.Workspace is { } workspace)
        {
            foreach (var match in scan.Shadowed)
            {
                var source = match.IsDirectory ? workspace.StubDirectoryPath : workspace.StubFilePath;
                volumes.Add(new BindMount(source, $"{WorkspaceTarget}/{match.RelativePath}", ReadOnly: true));
            }
        }

        return new ComposeSpec
        {
            Image = request.Image,
            Tmpfs = DefaultTmpfs,
            Volumes = volumes,
            Command = request.ClaudeArgs,
            Labels = request.Labels,
        };
    }
}
