using Docker.DotNet.Models;

namespace C3Launcher.Core.Docker;

public sealed record SessionInfo(
    string ContainerId,
    string Name,
    string SessionId,
    string ProjectPath,
    string ProjectName,
    string State,
    string Status,
    string Command,
    DateTime CreatedUtc)
{
    public TimeSpan Uptime => DateTime.UtcNow - CreatedUtc;

    public static SessionInfo From(ContainerListResponse container)
    {
        var labels = container.Labels ?? new Dictionary<string, string>();
        labels.TryGetValue(SessionLabels.SessionId, out var sessionId);
        labels.TryGetValue(SessionLabels.ProjectPath, out var projectPath);
        labels.TryGetValue(SessionLabels.ProjectName, out var projectName);

        var name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID;

        return new SessionInfo(
            container.ID,
            name,
            sessionId ?? string.Empty,
            projectPath ?? string.Empty,
            projectName ?? name,
            container.State,
            container.Status,
            container.Command,
            container.Created.ToUniversalTime());
    }
}

public sealed record SessionSample(
    double CpuPercent,
    ulong MemoryBytes,
    ulong MemoryLimitBytes,
    ulong NetworkBytes,
    ulong Pids)
{
    public double MemoryPercent => MemoryLimitBytes == 0 ? 0 : MemoryBytes * 100.0 / MemoryLimitBytes;
}

public sealed record SessionProcesses(IReadOnlyList<string> Titles, IReadOnlyList<IReadOnlyList<string>> Rows);
