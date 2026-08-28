namespace C3Launcher.Core.Docker;

/// <summary>
/// Compose derives its container names from the (temporary) compose file's
/// directory, so labels are the only stable way to find our own containers.
/// </summary>
public static class SessionLabels
{
    public const string SessionId = "com.c3launcher.session";
    public const string ProjectPath = "com.c3launcher.project";
    public const string ProjectName = "com.c3launcher.name";

    public static IReadOnlyDictionary<string, string> For(string sessionId, string projectPath, string projectName) =>
        new Dictionary<string, string>
        {
            [SessionId] = sessionId,
            [ProjectPath] = projectPath,
            [ProjectName] = projectName,
        };
}
