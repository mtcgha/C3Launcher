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

            var match = VersionPattern().Match(string.Join('\n', progress.Items));
            return match.Success ? match.Groups[1].Value : null;
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

    [GeneratedRegex(@"(\d+\.\d+\.\d+)")]
    private static partial Regex VersionPattern();
}
