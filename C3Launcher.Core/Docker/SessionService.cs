using Docker.DotNet;
using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

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

    public Task StopAsync(string containerId, CancellationToken cancellationToken = default) =>
        _client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
            cancellationToken);
}
