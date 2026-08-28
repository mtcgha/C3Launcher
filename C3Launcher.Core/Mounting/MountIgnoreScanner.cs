using System.IO.Enumeration;

namespace C3Launcher.Core.Mounting;

public sealed class MountIgnoreScanner
{
    private const int ProgressInterval = 500;

    private static readonly string[] BuildCriticalNames =
    [
        "package.json",
        "package-lock.json",
        "tsconfig.json",
        "*.csproj",
        "*.fsproj",
        "*.vbproj",
        "*.sln",
        "*.slnx",
        "pyproject.toml",
        "requirements.txt",
        "go.mod",
        "Cargo.toml",
        "composer.json",
        "global.json",
        "nuget.config",
    ];

    public MountScanResult Scan(
        string projectRoot,
        IReadOnlyList<MountIgnorePattern> patterns,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));

        if (patterns.Count == 0)
            return Empty(root);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchType = MatchType.Simple,
        };

        var matches = new List<MountIgnoreMatch>();
        var counts = patterns.ToDictionary(p => p, _ => 0);
        var grouped = patterns.ToDictionary(p => p, _ => new List<MountIgnoreMatch>());
        var scanned = 0;

        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++scanned % ProgressInterval == 0)
                progress?.Report(scanned);

            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            var relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
            if (relative.Length == 0 || relative == ".")
                continue;

            MountIgnoreMatch? match = null;

            foreach (var pattern in patterns)
            {
                if (!pattern.Matches(info.Name, relative, isDirectory))
                    continue;

                match ??= new MountIgnoreMatch(info.FullName, relative, isDirectory, pattern);
                counts[pattern]++;
                grouped[pattern].Add(match);
            }

            if (match is not null)
                matches.Add(match);
        }

        progress?.Report(scanned);

        matches.Sort(static (a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));

        return new MountScanResult
        {
            ProjectRoot = root,
            EntriesScanned = scanned,
            Matches = matches,
            Shadowed = FilterCoveredByShadowedDirectory(root, matches),
            Groups = [.. patterns.Select(p => new PatternGroup(p, counts[p], grouped[p]))],
            BuildCriticalWarnings = FindBuildCriticalWarnings(matches),
        };
    }

    /// <summary>
    /// A directory stub makes that directory appear empty inside the container, so
    /// anything beneath it no longer exists at mount time. Docker then tries to
    /// create the mountpoint in the read-only overlay and fails with
    /// "read-only file system". Drop those descendants.
    /// </summary>
    private static IReadOnlyList<MountIgnoreMatch> FilterCoveredByShadowedDirectory(
        string root,
        IReadOnlyList<MountIgnoreMatch> matches)
    {
        var shadowedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (match.IsDirectory)
                shadowedDirectories.Add(Path.TrimEndingDirectorySeparator(match.FullPath));
        }

        if (shadowedDirectories.Count == 0)
            return matches;

        var kept = new List<MountIgnoreMatch>(matches.Count);
        foreach (var match in matches)
        {
            if (!HasShadowedAncestor(match.FullPath, root, shadowedDirectories))
                kept.Add(match);
        }

        return kept;
    }

    private static bool HasShadowedAncestor(string fullPath, string root, HashSet<string> shadowedDirectories)
    {
        var ancestor = Path.GetDirectoryName(fullPath);

        while (!string.IsNullOrEmpty(ancestor))
        {
            ancestor = Path.TrimEndingDirectorySeparator(ancestor);
            if (ancestor.Length <= root.Length)
                return false;

            if (shadowedDirectories.Contains(ancestor))
                return true;

            var parent = Path.GetDirectoryName(ancestor);
            if (string.IsNullOrEmpty(parent) || parent == ancestor)
                return false;

            ancestor = parent;
        }

        return false;
    }

    private static IReadOnlyList<BuildCriticalWarning> FindBuildCriticalWarnings(
        IReadOnlyList<MountIgnoreMatch> matches)
    {
        var warnings = new List<BuildCriticalWarning>();

        foreach (var match in matches)
        {
            if (match.IsDirectory)
                continue;

            var name = Path.GetFileName(match.RelativePath);
            foreach (var critical in BuildCriticalNames)
            {
                if (FileSystemName.MatchesSimpleExpression(critical, name, ignoreCase: true))
                {
                    warnings.Add(new BuildCriticalWarning(match.RelativePath, match.Pattern));
                    break;
                }
            }
        }

        return warnings;
    }

    private static MountScanResult Empty(string root) => new()
    {
        ProjectRoot = root,
        EntriesScanned = 0,
        Matches = [],
        Shadowed = [],
        Groups = [],
        BuildCriticalWarnings = [],
    };
}
