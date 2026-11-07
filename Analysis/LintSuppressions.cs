using System.Text.RegularExpressions;

namespace SqlDeps;

/// <summary>Applies additive line, file, and repository directives to lint findings.</summary>
public static partial class LintSuppressions
{
    [GeneratedRegex("^\\s*--\\s*sqldeps:\\s*disable-next-line\\s*=\\s*(?<rules>[a-z0-9-]+(?:\\s*,\\s*[a-z0-9-]+)*)\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NextLineDirective();

    [GeneratedRegex("^\\s*--\\s*sqldeps:\\s*disable-file\\s*=\\s*(?<rules>[a-z0-9-]+(?:\\s*,\\s*[a-z0-9-]+)*)\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FileDirective();

    [GeneratedRegex("^\\s*(?:#|--)?\\s*sqldeps:\\s*disable-repo\\s*=\\s*(?<rules>[a-z0-9-]+(?:\\s*,\\s*[a-z0-9-]+)*)\\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryDirective();

    public static IEnumerable<LintFinding> Filter(
        IEnumerable<LintFinding> findings,
        string directiveText,
        int startLine,
        string? fullDocumentText = null,
        string? repositoryRoot = null)
    {
        var lines = (fullDocumentText ?? directiveText).Split('\n');
        var fileRules = RulesFrom(lines, FileDirective());
        var repositoryRules = RepositoryRules(repositoryRoot);
        var lineRules = LineRules(lines, startLine, fullDocumentText is not null);

        return findings.Where(finding => !IsSuppressed(
            finding,
            fileRules,
            repositoryRules,
            lineRules));
    }

    public static bool HasFileSuppression(string text, string rule)
    {
        return IsRuleDisabled(RulesFrom(text.Split('\n'), FileDirective()), rule);
    }

    public static bool HasRepositorySuppression(string text, string rule)
    {
        return IsRuleDisabled(RulesFrom(text.Split('\n'), RepositoryDirective()), rule);
    }

    private static bool IsSuppressed(
        LintFinding finding,
        IReadOnlySet<string> fileRules,
        IReadOnlySet<string> repositoryRules,
        IReadOnlyDictionary<int, HashSet<string>> lineRules)
    {
        if (IsRuleDisabled(fileRules, finding.Rule))
        {
            return true;
        }

        if (IsRuleDisabled(repositoryRules, finding.Rule))
        {
            return true;
        }

        return IsLineSuppressed(lineRules, finding);
    }

    private static bool IsLineSuppressed(
        IReadOnlyDictionary<int, HashSet<string>> lineRules,
        LintFinding finding)
    {
        return lineRules.TryGetValue(finding.Line, out var rules)
            && IsRuleDisabled(rules, finding.Rule);
    }

    private static bool IsRuleDisabled(IReadOnlySet<string> rules, string rule)
    {
        return rules.Contains("all") || rules.Contains(rule);
    }

    private static Dictionary<int, HashSet<string>> LineRules(
        string[] lines,
        int startLine,
        bool usesAbsoluteLines)
    {
        Dictionary<int, HashSet<string>> rulesByLine = new();

        for (var index = 0; index < lines.Length; index++)
        {
            var match = NextLineDirective().Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var targetLine = usesAbsoluteLines ? index + 2 : startLine + index + 1;
            rulesByLine[targetLine] = ParseRules(match.Groups["rules"].Value);
        }

        return rulesByLine;
    }

    private static HashSet<string> RulesFrom(string[] lines, Regex directive)
    {
        HashSet<string> rules = new(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var match = directive.Match(line);
            if (match.Success)
            {
                rules.UnionWith(ParseRules(match.Groups["rules"].Value));
            }
        }

        return rules;
    }

    private static HashSet<string> RepositoryRules(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return [];
        }

        var path = Path.Combine(repositoryRoot, ".sqldeps");
        if (!File.Exists(path))
        {
            return [];
        }

        return RulesFrom(File.ReadAllLines(path), RepositoryDirective());
    }

    private static HashSet<string> ParseRules(string value)
    {
        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
