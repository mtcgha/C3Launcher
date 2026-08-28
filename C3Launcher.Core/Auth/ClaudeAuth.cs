using System.Text.Json;

namespace C3Launcher.Core.Auth;

public sealed record ClaudeAuth(
    string Email,
    string? OrganizationName,
    string ClaudeDirectory,
    string ClaudeJsonPath);

public sealed record ClaudeAuthResult(ClaudeAuth? Auth, string? Error)
{
    public bool Ok => Auth is not null;

    public static ClaudeAuthResult Success(ClaudeAuth auth) => new(auth, null);
    public static ClaudeAuthResult Failure(string error) => new(null, error);
}

/// <summary>
/// Reads the account out of ~/.claude.json so an obvious problem (wrong account,
/// stale login) surfaces before the container starts. Token expiry is not checked
/// here — Claude Code refreshes internally.
/// </summary>
public static class ClaudeAuthReader
{
    private const string LoginHint = "Run 'claude login' on the host first.";

    public static ClaudeAuthResult Read(string? userProfile = null)
    {
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(userProfile))
            return ClaudeAuthResult.Failure("Could not determine the current user's profile directory.");

        var claudeDirectory = Path.Combine(userProfile, ".claude");
        var claudeJsonPath = Path.Combine(userProfile, ".claude.json");

        if (!Directory.Exists(claudeDirectory))
            return ClaudeAuthResult.Failure($"{claudeDirectory} not found. {LoginHint}");

        if (!File.Exists(claudeJsonPath))
            return ClaudeAuthResult.Failure($"{claudeJsonPath} not found. {LoginHint}");

        string json;
        try
        {
            json = File.ReadAllText(claudeJsonPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ClaudeAuthResult.Failure($"Could not read {claudeJsonPath}: {ex.Message}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ClaudeAuthResult.Failure($"{claudeJsonPath} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("oauthAccount", out var account)
                || account.ValueKind != JsonValueKind.Object)
            {
                return ClaudeAuthResult.Failure($"No account found in {claudeJsonPath}. {LoginHint}");
            }

            var email = GetString(account, "emailAddress");
            if (string.IsNullOrWhiteSpace(email))
                return ClaudeAuthResult.Failure($"No account found in {claudeJsonPath}. {LoginHint}");

            return ClaudeAuthResult.Success(new ClaudeAuth(
                email,
                GetString(account, "organizationName"),
                claudeDirectory,
                claudeJsonPath));
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
