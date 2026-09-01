using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

/// <summary>
/// The Docker named volume holding the container fleet's Claude Code home: its
/// login, settings and history, shared by every session.
///
/// The host's ~/.claude is deliberately not mounted. A refresh rotates the
/// refresh token and invalidates the previous one, so a container refreshing
/// against a copy of the host's credential silently kills the host's login —
/// which is why the host needed a fresh login every morning. Containers get
/// their own login in Docker-managed storage instead, so there is no shared
/// credential to synchronise and no host settings, hooks or history reachable
/// from inside a session.
///
/// One volume for all sessions, not one each: sessions that shared an account
/// but not a credential file would rotate each other's refresh token out from
/// under them. Sharing it makes concurrent sessions behave like several
/// terminals on one machine, which is the ordinary supported case.
/// </summary>
public static class ClaudeHomeVolume
{
    public const string VolumeName = "c3launcher-claude-home";
    public const string Target = "/home/node";

    private const string OwnerLabel = "com.c3launcher.volume";

    /// <summary>
    /// Creates the volume if it is missing. The compose file declares it
    /// external, so it has to exist before the first session starts.
    /// </summary>
    public static async Task EnsureAsync(IDockerClient client, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(client, cancellationToken))
            return;

        await client.Volumes.CreateAsync(
            new VolumesCreateParameters
            {
                Name = VolumeName,
                Labels = new Dictionary<string, string> { [OwnerLabel] = "claude-home" },
            },
            cancellationToken);
    }

    private static async Task<bool> ExistsAsync(IDockerClient client, CancellationToken cancellationToken)
    {
        var existing = await client.Volumes.ListAsync(cancellationToken);
        return existing.Volumes is { } volumes && volumes.Any(volume => volume.Name == VolumeName);
    }
}
