using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

public sealed record ClaudeUpdateStatus(
    string? InstalledVersion,
    NpmRelease? Latest,
    bool UpdateAvailable,
    string? Error)
{
    public static ClaudeUpdateStatus Failed(string error) => new(null, null, false, error);
}

public sealed partial class ClaudeUpdateService
{
    /// <summary>cc.ps1 ignores releases younger than this so a new version can soak.</summary>
    public static readonly TimeSpan DefaultSoak = TimeSpan.FromDays(7);

    private readonly IDockerClient _client;
    private readonly NpmRegistryClient _npm;

    public ClaudeUpdateService(IDockerClient client, NpmRegistryClient npm)
    {
        _client = client;
        _npm = npm;
    }

    public async Task<ClaudeUpdateStatus> CheckAsync(
        string image,
        TimeSpan? soak = null,
        CancellationToken cancellationToken = default)
    {
        string? installed;
        try
        {
            installed = await GetInstalledVersionAsync(image, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ClaudeUpdateStatus.Failed($"Could not read the installed version: {ex.Message}");
        }

        NpmRelease? latest;
        try
        {
            latest = await _npm.GetLatestReleaseBeforeAsync(
                NpmRegistryClient.ClaudeCodePackage,
                DateTimeOffset.UtcNow - (soak ?? DefaultSoak),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ClaudeUpdateStatus(installed, null, false, "Update check failed (offline?), using existing image.");
        }

        var available = installed is not null
            && latest is { } release
            && System.Version.TryParse(installed, out var current)
            && release.Version > current;

        return new ClaudeUpdateStatus(installed, latest, available, null);
    }

    /// <summary>The newest release old enough to have soaked, or null when unreachable.</summary>
    public async Task<string?> GetSoakedVersionAsync(
        TimeSpan? soak = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await _npm.GetLatestReleaseBeforeAsync(
                NpmRegistryClient.ClaudeCodePackage,
                DateTimeOffset.UtcNow - (soak ?? DefaultSoak),
                cancellationToken);

            return release?.Version.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The version a build should pin. An explicitly approved update wins; otherwise
    /// the soak policy decides, which means upgrading to the newest sufficiently old
    /// release and holding at the installed one when there is nothing newer.
    ///
    /// Leaving this null would let the Dockerfile's `CLAUDE_VERSION=latest` default
    /// choose, and npm's latest can be a release the soak would have rejected — so
    /// null comes back only when nothing at all is knowable.
    /// </summary>
    public async Task<string?> ResolveBuildVersionAsync(
        string image,
        string? approved = null,
        TimeSpan? soak = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(approved))
            return approved;

        string? installed = null;
        try
        {
            installed = await GetInstalledVersionAsync(image, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // No image yet, or its version can't be read; the registry decides alone.
        }

        var soaked = await GetSoakedVersionAsync(soak, cancellationToken);

        if (installed is null)
            return soaked;

        return soaked is not null
            && System.Version.TryParse(soaked, out var candidate)
            && System.Version.TryParse(installed, out var current)
            && candidate > current
                ? soaked
                : installed;
    }

    /// <summary>
    /// Bypasses the entrypoint so no auth mounts are needed, matching
    /// cc.ps1's `docker run --entrypoint /usr/local/bin/claude ... --version`.
    /// </summary>
    public async Task<string?> GetInstalledVersionAsync(string image, CancellationToken cancellationToken = default)
    {
        var created = await _client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = image,
                Entrypoint = ["/usr/local/bin/claude"],
                Cmd = ["--version"],
                Tty = true,
                AttachStdout = true,
                AttachStderr = true,
            },
            cancellationToken);

        try
        {
            await _client.Containers.StartContainerAsync(created.ID, null, cancellationToken);
            await _client.Containers.WaitContainerAsync(created.ID, cancellationToken);

            var progress = new CollectingProgress<string>();
            await _client.Containers.GetContainerLogsAsync(
                created.ID,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true },
                progress,
                cancellationToken);

            return ParseVersion(progress.Items);
        }
        finally
        {
            try
            {
                await _client.Containers.RemoveContainerAsync(
                    created.ID,
                    new ContainerRemoveParameters { Force = true },
                    CancellationToken.None);
            }
            catch (DockerApiException) { }
        }
    }

    /// <summary>
    /// `claude --version` prints "2.1.250 (Claude Code)", but a TTY merges stderr
    /// into the same stream, so an update notice naming a different version can
    /// share it. Take the line that looks like the real answer, and only fall back
    /// to a leading version — never a number from mid-sentence — if the wording
    /// changes upstream.
    /// </summary>
    private static string? ParseVersion(IReadOnlyList<string> output)
    {
        // The probe runs with a TTY, so the CLI may colour its output. Both patterns
        // below are anchored, and a leading escape sequence would defeat them.
        var lines = string.Join('\n', output)
            .Split('\n')
            .Select(line => AnsiEscapePattern().Replace(line, string.Empty).Trim())
            .ToArray();

        foreach (var line in lines)
        {
            if (ClaudeVersionPattern().Match(line) is { Success: true } exact)
                return exact.Groups[1].Value;
        }

        foreach (var line in lines)
        {
            if (LeadingVersionPattern().Match(line) is { Success: true } leading)
                return leading.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+)\S*\s+\(Claude Code\)", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudeVersionPattern();

    [GeneratedRegex(@"^v?(\d+\.\d+\.\d+)")]
    private static partial Regex LeadingVersionPattern();

    /// <summary>Any ECMA-48 control sequence: escape, parameters, intermediates, final byte.</summary>
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapePattern();
}
