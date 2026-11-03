using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? ResultSetExplanationAction(LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("insert-exec-count" or "insert-exec-type"))
        {
            return null;
        }

        var objectName = ResultSetObjectNamePattern().Match(diagnostic.Message).Groups["object"].Value;
        var columns = AllResultColumns()
            .Where(column => string.Equals(column.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var complete = columns.Length > 0 && columns.All(column => column.IsKnownName && column.DataType is not null);
        var reason = complete
            ? "INSERT ... EXEC result-set metadata is known, but no automatic structural edit is safe for this contract."
            : "INSERT ... EXEC result-set metadata is partial or unknown; sqldeps will not generate an automatic edit.";

        var metadata = DiagnosticMetadataMapper.For("sqldeps", diagnostic.Rule, "warning");
        return new
        {
            title = "Review INSERT ... EXEC result-set contract",
            kind = "quickfix",
            disabled = new { reason },
            data = new
            {
                source = metadata.Source,
                confidence = "low",
                diagnosticConfidence = diagnostic.Confidence,
                stale = metadata.IsStale,
                generation = "explanation-only",
                resultSetMetadata = complete ? "complete" : "partial-or-unknown",
            },
        };
    }

    [GeneratedRegex(@"procedure '(?<object>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex ResultSetObjectNamePattern();
}
