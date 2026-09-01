using System.Net;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

/// <summary>
/// Stopping a container the daemon has never heard of is not a failure worth
/// reporting. A Docker Desktop restart — an update, a daemon crash — takes every
/// container with it, and no die event survives the reconnect, so the caller's
/// list can still name a container that is long gone. <see cref="AlreadyGone"/>
/// tells it to drop the row rather than show an error.
/// </summary>
public sealed record StopOutcome(bool Ok, bool AlreadyGone, string? Error)
{
    public static readonly StopOutcome Stopped = new(true, false, null);

    public static readonly StopOutcome Gone = new(true, true, null);

    public static StopOutcome Failed(string error) => new(false, false, error);
}

public sealed class SessionService
{
    private readonly IDockerClient _client;

    public SessionService(IDockerClient client) => _client = client;

    public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool> { [SessionLabels.SessionId] = true },
                },
            },
            cancellationToken);

        return [.. containers.Select(SessionInfo.From).OrderByDescending(s => s.CreatedUtc)];
    }

    public async Task<SessionSample?> SampleAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var progress = new CollectingProgress<ContainerStatsResponse>();

        // Stream:false still returns precpu_stats, which the CPU delta needs.
        // OneShot would zero them out.
        await _client.Containers.GetContainerStatsAsync(
            containerId,
            new ContainerStatsParameters { Stream = false },
            progress,
            cancellationToken);

        var stats = progress.Items.LastOrDefault();
        return stats is null ? null : ToSample(stats);
    }

    public static SessionSample ToSample(ContainerStatsResponse stats)
    {
        var current = stats.CPUStats;
        var previous = stats.PreCPUStats;
        var memory = stats.MemoryStats;

        var cpuPercent = 0.0;

        if (current?.CPUUsage is { } usage && previous?.CPUUsage is { } previousUsage)
        {
            var cpuDelta = (double)usage.TotalUsage - previousUsage.TotalUsage;
            var systemDelta = (double)(current.SystemUsage ?? 0) - (previous.SystemUsage ?? 0);
            var cpuCount = current.OnlineCPUs ?? (uint?)usage.PercpuUsage?.Count ?? 1u;

            if (cpuDelta > 0 && systemDelta > 0)
                cpuPercent = cpuDelta / systemDelta * cpuCount * 100.0;
        }

        var used = memory?.Usage ?? 0;
        if (memory?.Stats is { } breakdown
            && breakdown.TryGetValue("inactive_file", out var inactiveFile)
            && inactiveFile < used)
        {
            used -= inactiveFile;
        }

        var network = 0UL;
        if (stats.Networks is { } interfaces)
        {
            foreach (var nic in interfaces.Values)
                network += nic.RxBytes + nic.TxBytes;
        }

        return new SessionSample(cpuPercent, used, memory?.Limit ?? 0, network, stats.PidsStats?.Current ?? 0);
    }

    public async Task<SessionProcesses> ListProcessesAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var response = await _client.Containers.ListProcessesAsync(
            containerId,
            new ContainerListProcessesParameters(),
            cancellationToken);

        return new SessionProcesses(
            response.Titles?.ToArray() ?? [],
            response.Processes?.Select(row => (IReadOnlyList<string>)row.ToArray()).ToArray() ?? []);
    }

    public Task<ContainerInspectResponse> InspectAsync(string containerId, CancellationToken cancellationToken = default) =>
        _client.Containers.InspectContainerAsync(containerId, cancellationToken);

    public async Task<StopOutcome> StopAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                cancellationToken);

            return StopOutcome.Stopped;
        }
        catch (DockerContainerNotFoundException)
        {
            return StopOutcome.Gone;
        }
        catch (DockerApiException ex) when (ex.StatusCode is HttpStatusCode.NotFound)
        {
            return StopOutcome.Gone;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DockerApiException ex)
        {
            return StopOutcome.Failed(Describe(ex));
        }
        catch (Exception ex)
        {
            // Not an answer from the daemon at all — a dropped named pipe or a
            // refused connection, which is what a restart looks like mid-request.
            return StopOutcome.Failed($"Docker did not respond: {ex.Message}");
        }
    }

    /// <summary>
    /// DockerApiException.Message is the status code plus the raw response body.
    /// The daemon's own one-line reason is inside that body, and it is the only
    /// part worth putting in front of a user.
    /// </summary>
    private static string Describe(DockerApiException ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            try
            {
                using var body = JsonDocument.Parse(ex.ResponseBody);

                if (body.RootElement.TryGetProperty("message", out var message)
                    && message.GetString() is { Length: > 0 } reason)
                {
                    return reason;
                }
            }
            catch (JsonException)
            {
                // Not the daemon's usual error shape; fall through to the code.
            }
        }

        return $"Docker returned {(int)ex.StatusCode} ({ex.StatusCode})";
    }
}
