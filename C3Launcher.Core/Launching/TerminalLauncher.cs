using System.ComponentModel;
using System.Diagnostics;

namespace C3Launcher.Core.Launching;

public sealed record TerminalLaunch(bool Ok, string? Error, bool UsedWindowsTerminal);

/// <summary>
/// Claude Code is a full-screen TUI, so the session runs in a real terminal
/// rather than anything embedded. Note wt.exe returns immediately — it hands the
/// request to the existing Windows Terminal and exits — so its process exit says
/// nothing about session lifetime. Track that through container events instead.
/// </summary>
public static class TerminalLauncher
{
    public static TerminalLaunch Launch(
        string composeFilePath,
        string serviceName,
        string title,
        string workingDirectory)
    {
        string[] compose = ["compose", "-f", composeFilePath, "run", "--rm", serviceName];

        if (TryStart("wt.exe", ["-w", "0", "new-tab", "--title", title, "-d", workingDirectory, "docker", .. compose], workingDirectory, out var error))
            return new TerminalLaunch(true, null, true);

        // No Windows Terminal. A WinExe has no console of its own, so starting a
        // console app without CREATE_NO_WINDOW gives it a fresh console window.
        if (TryStart("docker", compose, workingDirectory, out var fallbackError))
            return new TerminalLaunch(true, null, false);

        return new TerminalLaunch(false, fallbackError ?? error, false);
    }

    private static bool TryStart(string fileName, IEnumerable<string> arguments, string workingDirectory, out string? error)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = workingDirectory,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            error = null;
            return process is not null;
        }
        catch (Win32Exception ex)
        {
            error = $"Could not start '{fileName}': {ex.Message}";
            return false;
        }
    }
}
