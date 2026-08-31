using C3Launcher.Core.Auth;
using C3Launcher.Core.Compose;
using C3Launcher.Core.Docker;
using C3Launcher.Core.Mounting;

namespace C3Launcher.Core.Launching;

public sealed class LaunchOptions
{
    public required string ProjectPath { get; init; }
    public string? ProjectName { get; init; }
    public string? Model { get; init; }
    public string? PermissionMode { get; init; }
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];
    public string Image { get; init; } = ContainerAssets.ImageName;
    public string? MountIgnorePath { get; init; }
}

/// <summary>
/// Owns the temp files a session depends on. Dispose once the container's 'die'
/// event arrives — not when the launching process exits. If the launcher never
/// gets that far, SessionWorkspace.Sweep cleans up on the next start.
/// </summary>
public sealed class LaunchedSession : IDisposable
{
    private readonly SessionWorkspace _workspace;

    internal LaunchedSession(
        string projectPath,
        string projectName,
        SessionWorkspace workspace,
        MountScanResult scan,
        ClaudeAuth auth)
    {
        ProjectPath = projectPath;
        ProjectName = projectName;
        _workspace = workspace;
        Scan = scan;
        Auth = auth;
    }

    public string SessionId => _workspace.SessionId;
    public string ComposeFilePath => _workspace.ComposeFilePath;
    public string ProjectPath { get; }
    public string ProjectName { get; }
    public MountScanResult Scan { get; }
    public ClaudeAuth Auth { get; }

    public void Dispose() => _workspace.Dispose();
}

public sealed record LaunchOutcome(LaunchedSession? Session, string? Error)
{
    public bool Ok => Session is not null;

    public static LaunchOutcome Failed(string error) => new(null, error);
}

public sealed class SessionLauncher
{
    public const string ServiceName = "claude";

    private readonly MountIgnoreScanner _scanner = new();

    /// <summary>Prepares the session, then opens it in a terminal.</summary>
    public async Task<LaunchOutcome> LaunchAsync(
        LaunchOptions options,
        IProgress<int>? scanProgress = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await PrepareAsync(options, scanProgress, cancellationToken);
        if (!outcome.Ok)
            return outcome;

        var session = outcome.Session!;
        var launch = TerminalLauncher.Launch(
            session.ComposeFilePath,
            ServiceName,
            $"cc: {session.ProjectName}",
            session.ProjectPath);

        if (launch.Ok)
            return outcome;

        session.Dispose();
        return LaunchOutcome.Failed(launch.Error!);
    }

    /// <summary>
    /// Everything up to running the container. The caller owns the returned
    /// session and must dispose it once the container is gone.
    /// </summary>
    public async Task<LaunchOutcome> PrepareAsync(
        LaunchOptions options,
        IProgress<int>? scanProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!ProjectPath.TryResolve(options.ProjectPath, out var root, out var pathError))
            return LaunchOutcome.Failed(pathError!);

        var auth = ClaudeAuthReader.Read();
        if (!auth.Ok)
            return LaunchOutcome.Failed(auth.Error!);

        var patterns = MountIgnorePattern.ParseFile(options.MountIgnorePath ?? ContainerAssets.DefaultMountIgnorePath);

        var scan = await Task.Run(
            () => _scanner.Scan(root, patterns, scanProgress, cancellationToken),
            cancellationToken);

        var projectName = options.ProjectName ?? Path.GetFileName(root);
        if (string.IsNullOrEmpty(projectName))
            projectName = root;

        var sessionId = Guid.NewGuid().ToString("n");
        var workspace = SessionWorkspace.Create(sessionId);

        try
        {
            var spec = ComposePlanner.Build(new ComposePlanRequest
            {
                ProjectRoot = root,
                Auth = auth.Auth!,
                Image = options.Image,
                Scan = scan,
                Workspace = workspace,
                ClaudeArgs = BuildClaudeArgs(options),
                Labels = SessionLabels.For(sessionId, root, projectName),
                Network = new ComposeNetwork(
                    SessionNetworks.NameFor(sessionId),
                    SessionNetworks.LabelsFor(sessionId)),
            });

            ComposeWriter.WriteTo(spec, workspace.ComposeFilePath);

            return new LaunchOutcome(
                new LaunchedSession(root, projectName, workspace, scan, auth.Auth!),
                null);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<string> BuildClaudeArgs(LaunchOptions options)
    {
        var args = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            args.Add("--model");
            args.Add(options.Model);
        }

        if (!string.IsNullOrWhiteSpace(options.PermissionMode))
        {
            args.Add("--permission-mode");
            args.Add(options.PermissionMode);
        }

        args.AddRange(options.ExtraArgs);
        return args;
    }
}
