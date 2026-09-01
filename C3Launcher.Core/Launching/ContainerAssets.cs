namespace C3Launcher.Core.Launching;

/// <summary>
/// The Docker build context, shipped alongside the executable.
/// </summary>
public static class ContainerAssets
{
    public const string ImageName = "claude-locked";

    public static string BuildContextPath { get; } = Path.Combine(AppContext.BaseDirectory, "Container");

    public static string DockerfilePath => Path.Combine(BuildContextPath, "Dockerfile");

    public static string EntrypointPath => Path.Combine(BuildContextPath, "entrypoint.sh");

    public static string ManagedPolicyPath => Path.Combine(BuildContextPath, "container-CLAUDE.md");

    public static string ManagedSettingsPath => Path.Combine(BuildContextPath, "managed-settings.json");

    public static string DefaultMountIgnorePath => Path.Combine(BuildContextPath, ".mountignore");

    public static bool IsPresent => File.Exists(DockerfilePath) && File.Exists(EntrypointPath);
}
