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

        var table = AllObjects()
            .Select(item => item.Object)
            .FirstOrDefault(item => SameName(item.Name, tableName));

        if (table is null || table.Kind is not ("table" or "view"))
        {
            return null;
        }

        var columnExists = AllTableColumns().Any(column =>
            SameName(column.TableName, table.Name)
            && SameName(column.ColumnName, columnName));

        if (!columnExists)
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
                diagnosticConfidence = diagnostic.Confidence,
                experimental = true,
                rule = diagnostic.Rule,
                applicability = "The selected table and column must exist in the indexed metadata.",
                preview = true,
                writesFile = false,
                references = ReferencesFor(diagnostic.Rule),
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
