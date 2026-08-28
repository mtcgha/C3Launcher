using C3Launcher.Core.Mounting;

namespace C3Launcher.Gui.ViewModels;

public sealed class PatternRowViewModel
{
    public required string Pattern { get; init; }
    public required int Count { get; init; }
    public required string Examples { get; init; }
    public required bool IsBuildCritical { get; init; }

    public bool HasMatches => Count > 0;
    public double RowOpacity => Count > 0 ? 1.0 : 0.5;
}

public sealed class MountPreviewViewModel : ViewModelBase
{
    private const int ExampleCount = 3;

    public MountPreviewViewModel(MountScanResult scan, string mountIgnorePath)
    {
        MountIgnorePath = mountIgnorePath;
        ProjectName = System.IO.Path.GetFileName(scan.ProjectRoot);

        var criticalPatterns = scan.BuildCriticalWarnings
            .Select(w => w.Pattern)
            .ToHashSet();

        Patterns =
        [
            .. scan.Groups
                .OrderByDescending(g => g.MatchCount)
                .Select(g => new PatternRowViewModel
                {
                    Pattern = g.Pattern.Raw,
                    Count = g.MatchCount,
                    Examples = g.MatchCount == 0
                        ? "no matches in this project"
                        : string.Join(", ", g.Matches.Take(ExampleCount).Select(m => m.RelativePath))
                          + (g.MatchCount > ExampleCount ? "…" : string.Empty),
                    IsBuildCritical = criticalPatterns.Contains(g.Pattern),
                }),
        ];

        BuildCriticalFiles =
        [
            .. scan.BuildCriticalWarnings
                .Select(w => System.IO.Path.GetFileName(w.RelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        var offendingPattern = scan.BuildCriticalWarnings.FirstOrDefault()?.Pattern.Raw;

        WarningHeadline = BuildCriticalFiles.Count == 1
            ? "1 build-critical file will be hidden from Claude"
            : $"{BuildCriticalFiles.Count} build-critical files will be hidden from Claude";

        WarningDetail = offendingPattern is null
            ? "Builds and installs inside the container may fail as a result."
            : $"Builds and installs inside the container may fail as a result. This is usually intentional — the pattern {offendingPattern} is what catches them.";

        Summary = $"matched by {Plural(scan.Groups.Count, "pattern")} · "
            + $"{Plural(scan.ShadowedCount, "stub mount")} after nesting is collapsed";

        MatchCountText = Plural(scan.MatchCount, "item");
    }

    public string ProjectName { get; }
    public string MountIgnorePath { get; }
    public string MatchCountText { get; }
    public string Summary { get; }
    public string WarningHeadline { get; }
    public string WarningDetail { get; }
    public IReadOnlyList<PatternRowViewModel> Patterns { get; }
    public IReadOnlyList<string> BuildCriticalFiles { get; }

    public bool HasWarning => BuildCriticalFiles.Count > 0;

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
