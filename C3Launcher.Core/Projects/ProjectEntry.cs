namespace C3Launcher.Core.Projects;

public sealed class ProjectEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Pinned { get; set; }

    /// <summary>1-9 for the Ctrl+N accelerators; null when unpinned.</summary>
    public int? PinSlot { get; set; }

    public string? Model { get; set; }
    public string? ExtraArgs { get; set; }
    public bool SkipUpdateCheck { get; set; }

    /// <summary>Overrides the shipped default .mountignore when set.</summary>
    public string? MountIgnorePath { get; set; }

    public DateTime? LastOpenedUtc { get; set; }
    public int LaunchCount { get; set; }
}

public sealed class LauncherSettings
{
    public string Image { get; set; } = "claude-locked";
    public string? DockerEndpoint { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public int UpdateSoakDays { get; set; } = 7;
    public bool SessionsExpanded { get; set; }
    public List<ProjectEntry> Projects { get; set; } = [];
}
