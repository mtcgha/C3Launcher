using System.IO.Enumeration;

namespace C3Launcher.Core.Mounting;

public sealed class MountIgnorePattern
{
    private MountIgnorePattern(string raw, string expression, bool directoriesOnly)
    {
        Raw = raw;
        Expression = expression;
        DirectoriesOnly = directoriesOnly;
    }

    public string Raw { get; }
    public string Expression { get; }
    public bool DirectoriesOnly { get; }

    public static bool TryParse(string line, out MountIgnorePattern? pattern)
    {
        pattern = null;

        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return false;

        var directoriesOnly = trimmed.EndsWith('/');
        var expression = trimmed.TrimEnd('/');
        if (expression.Length == 0)
            return false;

        pattern = new MountIgnorePattern(trimmed, expression, directoriesOnly);
        return true;
    }

    public static IReadOnlyList<MountIgnorePattern> Parse(IEnumerable<string> lines)
    {
        var patterns = new List<MountIgnorePattern>();
        foreach (var line in lines)
        {
            if (TryParse(line, out var pattern))
                patterns.Add(pattern!);
        }

        return patterns;
    }

    public static IReadOnlyList<MountIgnorePattern> ParseFile(string path) =>
        File.Exists(path) ? Parse(File.ReadLines(path)) : [];

    public bool Matches(string name, string relativePath, bool isDirectory)
    {
        if (DirectoriesOnly && !isDirectory)
            return false;

        return IsMatch(name) || IsMatch(relativePath);
    }

    // Stands in for PowerShell's -like. Divergence: -like supports [abc] character
    // classes and treats < > " literally; MatchesSimpleExpression is the reverse.
    private bool IsMatch(string value) =>
        FileSystemName.MatchesSimpleExpression(Expression, value, ignoreCase: true);

    public override string ToString() => Raw;
}
