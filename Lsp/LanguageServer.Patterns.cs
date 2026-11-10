using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    [GeneratedRegex(@"(?<alias>[\w\[\]]+)\.(?<prefix>[\w\[\]]*)$")]
    private static partial Regex AliasCompletionPattern();

    [GeneratedRegex(@"(?<name>(?:[\w\[\]]+\.)*[\w\[\]]*)$")]
    private static partial Regex ObjectCompletionPattern();

    [GeneratedRegex(@"\b(?:FROM|JOIN)\s+(?<table>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,3})(?:\s+(?:AS\s+)?(?<alias>\w+))?", RegexOptions.IgnoreCase)]
    private static partial Regex TableAliasPattern();

    [GeneratedRegex(@"\bSELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex SelectPattern();

    [GeneratedRegex(@"\bFROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"^\s*(?:(?<value>.+?)\s*(?<operator><>|!=|=)\s*NULL|NULL\s*(?<reverseOperator><>|!=|=)\s*(?<reverseValue>.+?))\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NullComparisonPattern();

    [GeneratedRegex(@"known as alias '(?<alias>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex AliasInDiagnosticPattern();

    [GeneratedRegex(@"exists in (?<candidates>.+)\. Qualify", RegexOptions.IgnoreCase)]
    private static partial Regex AmbiguousColumnCandidatesPattern();

    [GeneratedRegex(@"Did you mean '(?<suggestion>[^']+)'\?", RegexOptions.IgnoreCase)]
    private static partial Regex SuggestedColumnPattern();

    private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
