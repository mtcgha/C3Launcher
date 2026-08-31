using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

/// <summary>
/// Compose creates a network per project and `run --rm` removes only the
/// container, so every session would leak one. Each leftover holds a block of the
/// daemon's default address pool, and once that pool is exhausted new launches
/// fail with "could not find an available, non-overlapping IPv4 address pool".
///
/// Every method here is best-effort and never throws: cleanup must not be able to
/// break a launch or block startup. Whatever a run misses, the next sweep takes.
/// </summary>
public static class SessionNetworks
{
    private const string NamePrefix = "c3launcher-";

    /// <summary>
    /// Compose creates the network before it creates the container, so for a moment
    /// a perfectly healthy network has no session and no endpoints. Sweeping in that
    /// window kills the launch it belongs to, so anything this new is left alone.
    /// </summary>
    private static readonly TimeSpan CreationGrace = TimeSpan.FromMinutes(1);

    public static string NameFor(string sessionId) => NamePrefix + sessionId;

    public static IReadOnlyDictionary<string, string> LabelsFor(string sessionId) =>
        new Dictionary<string, string> { [SessionLabels.SessionId] = sessionId };

    /// <summary>
    /// Deletes one session's network. Call it on the container's 'destroy' event:
    /// while the container still exists the network has an active endpoint and the
    /// daemon refuses, leaving the leftover to the next sweep.
    /// </summary>
    public static Task<bool> DeleteAsync(
        IDockerClient client,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        TryDeleteAsync(client, NameFor(sessionId), cancellationToken);

    /// <summary>Deletes the networks of every session not in <paramref name="liveSessionIds"/>.</summary>
    public static async Task<int> SweepAsync(
        IDockerClient client,
        IEnumerable<string> liveSessionIds,
        CancellationToken cancellationToken = default)
    {
        var live = liveSessionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var networks = await ListAsync(client, cancellationToken);
        var swept = 0;

        foreach (var network in networks)
        {
            if (!IsOurs(network, out var sessionId) || live.Contains(sessionId))
                continue;

            // A timestamp the daemon reports in an unexpected kind could land in the
            // future; treat only a sane, recent age as "too new to judge", so a bad
            // clock leaves leftovers swept rather than accumulating forever.
            var age = DateTime.UtcNow - network.Created.ToUniversalTime();
            if (age >= TimeSpan.Zero && age < CreationGrace)
                continue;

            // Only populated by some daemon versions, so this narrows rather than
            // guards — the daemon itself refuses a network that is still in use.
            if (network.Containers is { Count: > 0 })
                continue;

            if (await TryDeleteAsync(client, network.Name, cancellationToken))
                swept++;
        }

        return swept;
    }

    /// <summary>
    /// A hex-named "*_default" network could belong to any compose project on the
    /// machine, so ownership takes both our label and the name we would have given
    /// it — not the shape of the name alone.
    /// </summary>
    private static bool IsOurs(NetworkResponse network, out string sessionId)
    {
        sessionId = string.Empty;

        if (network.Labels is null
            || !network.Labels.TryGetValue(SessionLabels.SessionId, out var id)
            || string.IsNullOrEmpty(id)
            || !string.Equals(network.Name, NameFor(id), StringComparison.Ordinal))
        {
            return false;
        }

        sessionId = id;
        return true;
    }

    private static async Task<IEnumerable<NetworkResponse>> ListAsync(
        IDockerClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.Networks.ListNetworksAsync(new NetworksListParameters(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static async Task<bool> TryDeleteAsync(
        IDockerClient client,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Networks.DeleteNetworkAsync(name, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Already gone, or still holding an endpoint; the next sweep gets it.
            return false;
        }
    }
}
