namespace C3Launcher.Core.Mounting;

public sealed record MountIgnoreMatch(
    string FullPath,
    string RelativePath,
    bool IsDirectory,
    MountIgnorePattern Pattern);

public sealed record PatternGroup(
    MountIgnorePattern Pattern,
    int MatchCount,
    IReadOnlyList<MountIgnoreMatch> Matches);

public sealed record BuildCriticalWarning(string RelativePath, MountIgnorePattern Pattern);

public sealed class MountScanResult
{
    public required string ProjectRoot { get; init; }
    public required int EntriesScanned { get; init; }

    /// <summary>Everything a pattern matched, deduplicated by path.</summary>
    public required IReadOnlyList<MountIgnoreMatch> Matches { get; init; }

    /// <summary>
    /// The subset that actually becomes a stub mount. Entries under an
    /// already-shadowed directory are excluded — see MountIgnoreScanner.
    /// </summary>
    public required IReadOnlyList<MountIgnoreMatch> Shadowed { get; init; }

    public required IReadOnlyList<PatternGroup> Groups { get; init; }

    public required IReadOnlyList<BuildCriticalWarning> BuildCriticalWarnings { get; init; }

    public int MatchCount => Matches.Count;
    public int ShadowedCount => Shadowed.Count;
}
