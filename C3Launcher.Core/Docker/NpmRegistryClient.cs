using System.Text.Json;

namespace C3Launcher.Core.Docker;

public sealed record NpmRelease(Version Version, DateTimeOffset Published);

public sealed class NpmRegistryClient
{
    public const string ClaudeCodePackage = "@anthropic-ai/claude-code";

    private readonly HttpClient _http;

    public NpmRegistryClient(HttpClient http) => _http = http;

    public static Uri PackageUri(string package) =>
        new($"https://registry.npmjs.org/{package.Replace("/", "%2f")}");

    /// <summary>
    /// Newest release published before <paramref name="cutoff"/>. cc.ps1 deliberately
    /// ignores anything younger than a week so a fresh release gets time to soak.
    /// </summary>
    public async Task<NpmRelease?> GetLatestReleaseBeforeAsync(
        string package,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await _http.GetStreamAsync(PackageUri(package), cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Object)
            return null;

        NpmRelease? newest = null;

        foreach (var entry in time.EnumerateObject())
        {
            if (!Version.TryParse(entry.Name, out var version) || version.Revision >= 0 || version.Build < 0)
                continue;

            if (entry.Value.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(entry.Value.GetString(), out var published)
                || published >= cutoff)
            {
                continue;
            }

            if (newest is null || version > newest.Version)
                newest = new NpmRelease(version, published);
        }

        return newest;
    }
}
