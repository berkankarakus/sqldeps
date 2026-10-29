using System.Text.RegularExpressions;

namespace SqlDeps;

/// <summary>Applies narrowly-scoped comments that suppress a lint rule on the following line.</summary>
internal static partial class LintSuppressions
{
    [GeneratedRegex("^\\s*--\\s*sqldeps:\\s*disable-next-line\\s*=\\s*(?<rules>[a-z0-9-]+(?:\\s*,\\s*[a-z0-9-]+)*)\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Directive();

    public static IEnumerable<LintFinding> Filter(IEnumerable<LintFinding> findings, string text, int startLine)
    {
        Dictionary<int, HashSet<string>> rulesByLine = new();
        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var match = Directive().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var targetLine = startLine + index + 1;
            rulesByLine[targetLine] = match.Groups["rules"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return findings.Where(finding => !rulesByLine.TryGetValue(finding.Line, out var rules)
            || (!rules.Contains("all") && !rules.Contains(finding.Rule)));
    }
}
