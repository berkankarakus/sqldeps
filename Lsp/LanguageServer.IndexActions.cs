using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? IndexSuggestionAction(LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("unindexed-where" or "unindexed-join" or "unindexed-order-by"))
        {

            return null;
        }

        var match = UnindexedColumnPattern().Match(diagnostic.Message);

        if (!match.Success)
        {

            return null;
        }

        var tableName = match.Groups["table"].Value;
        var columnName = match.Groups["column"].Value;

        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
        {

            return null;
        }

        var title = $"Preview index for {tableName}.{columnName}";

        return new
        {
            title,
            kind = "refactor",
            data = new
            {
                source = "sqldeps",
                confidence = diagnostic.Confidence,
                preview = true,
                writesFile = false,
            },
            command = new
            {
                title,
                command = "sqldeps.index-suggestion",
                arguments = new[] { tableName, columnName },
            },
        };
    }

    [GeneratedRegex(
        @"(?:WHERE filters|JOIN compares|ORDER BY sorts) '(?<table>.+)\.(?<column>[^.']+)'",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnindexedColumnPattern();
}
