namespace C3Launcher.Core.Launching;

public static class ProjectPath
{
    /// <summary>
    /// Replaces cc.ps1's Resolve-Path, which threw a raw stack trace on a typo.
    /// </summary>
    public static bool TryResolve(string? input, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "No project directory given.";
            return false;
        }

        var candidate = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));

        string resolved;
        try
        {
            resolved = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"'{input}' is not a usable path: {ex.Message}";
            return false;
        }

        if (File.Exists(resolved))
        {
            error = $"'{resolved}' is a file, not a directory.";
            return false;
        }

        if (!Directory.Exists(resolved))
        {
            error = $"Directory not found: {resolved}";
            return false;
        }

        fullPath = Path.TrimEndingDirectorySeparator(resolved);
        if (fullPath.Length == 0)
            fullPath = resolved;

        return true;
    }
}
