using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? AmbiguousTableAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "ambiguous-table")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var tableReference = TextInRange(text, diagnostic.Range);
        var match = AmbiguousTablePattern().Match(diagnostic.Message);

        if (tableReference is null)
        {
            return null;
        }

        if (!match.Success)
        {
            return null;
        }

        var tableName = match.Groups["name"].Value;
        var qualifiedName = match.Groups["qualified"].Value;

        if (!string.Equals(tableReference, tableName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!qualifiedName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return QuickFix(
            $"Qualify table as '{qualifiedName}'",
            uri,
            diagnostic.Range,
            qualifiedName,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The qualified table name must resolve to one indexed schema object.");
    }

    [GeneratedRegex(
        @"Table '(?<name>[^']+)' exists under multiple schemas .*?qualify it as '(?<qualified>[^']+)'",
        RegexOptions.IgnoreCase)]
    private static partial Regex AmbiguousTablePattern();
}
