using System.Diagnostics;
using C3Launcher.Core.Docker;
using C3Launcher.Core.Launching;

namespace C3Launcher.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] rawArgs)
    {
        var args = CliArgs.Parse(rawArgs);

        WriteHeader();

        if (args.ShowHelp)
        {
            WriteHelp();
            return 0;
        }

        if (!ProjectPath.TryResolve(args.ProjectPath ?? ".", out var root, out var pathError))
            return Fail(pathError!);

        Row("Workspace", root);

        using var docker = DockerConnection.Create();

        if (await docker.ProbeAsync() is { } dockerError)
            return Fail(dockerError);

        await SweepAsync(docker, args.Verbose);

        if (!args.SkipBuild)
        {
            Row("Building", $"{ContainerAssets.ImageName} image");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var updater = new ClaudeUpdateService(docker.Client, new NpmRegistryClient(http));
            var version = await updater.ResolveBuildVersionAsync(ContainerAssets.ImageName);

            var builder = new ImageBuildService(docker.Client);
            var outcome = await builder.BuildAsync(
                ContainerAssets.ImageName,
                version,
                args.Verbose ? new ConsoleProgress() : null);

            if (!outcome.Ok)
            {
                if (!await builder.ImageExistsAsync(ContainerAssets.ImageName))
                    return Fail($"Build failed and there is no existing image to fall back on. {outcome.Error}");

                Row("Build", "failed — falling back to the existing image");
            }
        }

        // The compose file declares the Claude home volume external, so it has
        // to exist before the session starts.
        try
        {
            await ClaudeHomeVolume.EnsureAsync(docker.Client);
        }
        catch (Exception ex)
        {
            return Fail($"Could not create the Claude home volume: {ex.Message}");
        }

        var launcher = new SessionLauncher();
        var prepared = await launcher.PrepareAsync(new LaunchOptions
        {
            ProjectPath = root,
            Model = args.Model,
            ExtraArgs = args.ClaudeArgs,
        });

        if (!prepared.Ok)
            return Fail(prepared.Error!);

        using var session = prepared.Session!;

        if (session.Scan.ShadowedCount > 0)
            Row("Ignored", $"{session.Scan.MatchCount} item(s) via .mountignore");

        Row("Starting", "session");
        Console.WriteLine();

        var exitCode = RunCompose(session);

        // The container is gone by now, so its network is free.
        await SessionNetworks.DeleteAsync(docker.Client, session.SessionId);

        return exitCode;
    }

    /// <summary>
    /// Collects the temp folders and networks of sessions whose containers are gone —
    /// ours from a previous run, or a GUI session that was killed before its cleanup ran.
    /// </summary>
    private static async Task SweepAsync(DockerConnection docker, bool verbose)
    {
        try
        {
            var live = await new SessionService(docker.Client).ListAsync();
            var liveSessionIds = live.Where(s => s.State is "running").Select(s => s.SessionId).ToList();

            var swept = SessionWorkspace.Sweep(liveSessionIds);
            var networks = await SessionNetworks.SweepAsync(docker.Client, liveSessionIds);

            if (verbose && swept > 0)
                Row("Swept", $"{swept} stale session folder(s)");

            if (verbose && networks > 0)
                Row("Swept", $"{networks} stale session network(s)");
        }
        catch (Exception ex)
        {
            if (verbose)
                Row("Sweep", $"skipped ({ex.Message})");
        }
    }

    private static int RunCompose(LaunchedSession session)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            WorkingDirectory = session.ProjectPath,
        };

        foreach (var argument in new[]
        {
            "compose", "-f", session.ComposeFilePath, "run", "--rm", SessionLauncher.ServiceName,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return Fail("Could not start docker.");

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            return Fail($"Could not start docker: {ex.Message}");
        }
    }

    private static void WriteHeader()
    {
        Console.WriteLine();
        Write("   /\\_/\\   ", ConsoleColor.Yellow);
        WriteLine(" Claude Code Secure Container", ConsoleColor.Cyan);
        Write("  ( ^.^ )  ", ConsoleColor.Yellow);
        WriteLine("                  (by Claude)", ConsoleColor.DarkGray);
        WriteLine("   > ~ <   ", ConsoleColor.Yellow);
        Console.WriteLine();
    }

    private static void WriteHelp()
    {
        WriteLine("  USAGE", ConsoleColor.Yellow);
        Console.Write("    ");
        Write("cc", ConsoleColor.Cyan);
        Console.WriteLine(" [ProjectPath] [--model <name>] [--no-build] [--verbose] [-- ClaudeArgs...]");
        Console.WriteLine();
        WriteLine("  PARAMETERS", ConsoleColor.Yellow);
        Help("ProjectPath", "Directory to open (default: the current directory).");
        Help("--model", "Model to pass through to Claude Code.");
        Help("--no-build", "Skip the image build and use whatever is already there.");
        Help("--verbose", "Print the image build output instead of hiding it.");
        Help("-- ...", "Everything after -- is forwarded verbatim to the claude binary.");
        Console.WriteLine();
        WriteLine("  For saved projects, session monitoring and mount previews, use the GUI.", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static void Help(string name, string description)
    {
        Console.Write("    ");
        Write($"{name,-14}", ConsoleColor.Cyan);
        Console.WriteLine(description);
    }

    private static void Row(string label, string value, string? trailing = null)
    {
        Write($"  {label,-11}", ConsoleColor.DarkGray);
        Write(value, ConsoleColor.White);

        if (string.IsNullOrEmpty(trailing))
            Console.WriteLine();
        else
            WriteLine($"  {trailing}", ConsoleColor.DarkGray);
    }

    private static int Fail(string message)
    {
        Console.WriteLine();
        WriteLine($"  X  {message}", ConsoleColor.Red);
        Console.WriteLine();
        return 1;
    }

    private static void Write(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    private static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    private sealed class ConsoleProgress : IProgress<string>
    {
        public void Report(string value) => WriteLine($"  {value}", ConsoleColor.DarkGray);
    }
}

internal sealed class CliArgs
{
    public string? ProjectPath { get; private init; }
    public string? Model { get; private init; }
    public bool SkipBuild { get; private init; }
    public bool Verbose { get; private init; }
    public bool ShowHelp { get; private init; }
    public IReadOnlyList<string> ClaudeArgs { get; private init; } = [];

    public static CliArgs Parse(string[] args)
    {
        var separator = Array.IndexOf(args, "--");
        var own = separator >= 0 ? args[..separator] : args;
        var forwarded = separator >= 0 ? args[(separator + 1)..] : [];

        string? path = null;
        string? model = null;
        var skipBuild = false;
        var verbose = false;
        var help = false;

        for (var i = 0; i < own.Length; i++)
        {
            switch (own[i])
            {
                case "-h" or "--help" or "-?" or "/?":
                    help = true;
                    break;
                case "--no-build":
                    skipBuild = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--model" when i + 1 < own.Length:
                    model = own[++i];
                    break;
                default:
                    path ??= own[i];
                    break;
            }
        }

        return new CliArgs
        {
            ProjectPath = path,
            Model = model,
            SkipBuild = skipBuild,
            Verbose = verbose,
            ShowHelp = help,
            ClaudeArgs = forwarded,
        };
    }
}
