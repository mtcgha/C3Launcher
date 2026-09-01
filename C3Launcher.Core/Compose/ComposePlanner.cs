using System.Security.Cryptography;
using System.Text;
using C3Launcher.Core.Docker;
using C3Launcher.Core.Launching;
using C3Launcher.Core.Mounting;

namespace C3Launcher.Core.Compose;

public sealed class ComposePlanRequest
{
    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public string Image { get; init; } = ContainerAssets.ImageName;
    public MountScanResult? Scan { get; init; }
    public SessionWorkspace? Workspace { get; init; }
    public IReadOnlyList<string> ClaudeArgs { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public ComposeNetwork? Network { get; init; }
}

public static class ComposePlanner
{
    public const string WorkspaceRoot = "/workspace";

    private const int MaxSlugLength = 32;
    private const int HashLength = 8;

    public static readonly IReadOnlyList<string> DefaultTmpfs =
    [
        "/tmp:rw,noexec,nosuid,size=256m",
    ];

    /// <summary>
    /// Where a project is mounted, and the session's working directory.
    ///
    /// Claude Code keys session transcripts, auto memory and trust decisions by
    /// the project's path, and auto memory is loaded at session start. Mounting
    /// every project at the same path would file all of them under one key, so
    /// one project's notes would load into a session on another.
    ///
    /// The hash keeps two projects that share a folder name apart. It comes from
    /// the full path rather than the name, so renaming the entry in the launcher
    /// does not orphan the project's history, and the same project resolves to
    /// the same path on every launch.
    /// </summary>
    public static string TargetFor(string projectRoot, string projectName) =>
        $"{WorkspaceRoot}/{Slug(projectName)}-{PathHash(projectRoot)}";

    public static ComposeSpec Build(ComposePlanRequest request)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.ProjectRoot));
        var target = TargetFor(root, request.ProjectName);

        var volumes = new List<BindMount>
        {
            new(root, target, ReadOnly: false),
        };

        // More-specific targets added after the project take precedence, so each
        // stub hides the real path from inside the container.
        if (request.Scan is { } scan && request.Workspace is { } workspace)
        {
            foreach (var match in scan.Shadowed)
            {
                var source = match.IsDirectory ? workspace.StubDirectoryPath : workspace.StubFilePath;
                volumes.Add(new BindMount(source, $"{target}/{match.RelativePath}", ReadOnly: true));
            }
        }

        var environment = new Dictionary<string, string>();

        if (HostTimeZoneId() is { } timeZone)
            environment["TZ"] = timeZone;

        return new ComposeSpec
        {
            Image = request.Image,
            WorkingDirectory = target,
            Environment = environment,
            Tmpfs = DefaultTmpfs,
            Volumes = volumes,
            // The container's Claude login lives here, shared by every session
            // and owned by no host path. Nothing from the host's ~/.claude is
            // mounted: the host keeps its own login, and the two never rotate
            // each other's refresh token out from under them.
            NamedVolumes = [new NamedVolume(ClaudeHomeVolume.VolumeName, ClaudeHomeVolume.Target)],
            Command = request.ClaudeArgs,
            Labels = request.Labels,
            Network = request.Network,
        };
    }

    /// <summary>
    /// The image is Debian, which runs on UTC, so a session's timestamps would
    /// not match the clock the user is reading them against. glibc and Node both
    /// want the IANA name, while a Windows host reports its own id, hence the
    /// conversion. Null when nothing maps, which leaves the container on UTC
    /// rather than guessing at an offset.
    /// </summary>
    private static string? HostTimeZoneId()
    {
        var local = TimeZoneInfo.Local.Id;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(local, out var iana))
            return iana;

        // Already an IANA id when the launcher is not running on Windows.
        return local.Contains('/') ? local : null;
    }

    private static string Slug(string projectName)
    {
        var builder = new StringBuilder(projectName.Length);

        foreach (var character in projectName)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxSlugLength)
            slug = slug[..MaxSlugLength].TrimEnd('-');

        return slug.Length == 0 ? "project" : slug;
    }

    /// <summary>Case-insensitive, because the paths it identifies are Windows paths.</summary>
    private static string PathHash(string projectRoot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectRoot.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..HashLength].ToLowerInvariant();
    }
}
