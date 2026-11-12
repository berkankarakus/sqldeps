namespace SqlDeps.Core;

/// <summary>
/// Runs the layout phases over one formatted batch. Every phase reads a fresh parse of the previous result and a
/// phase whose output no longer parses is skipped, so the batch always stays valid T-SQL.
/// </summary>
internal static class SqlLayoutPipeline
{
    private static readonly Func<SqlLayoutDocument, string?>[] LayoutPhases =
    [
        SqlStatementLayoutVisitor.Apply,
        SqlSingleSelectLayout.Apply,
        SqlJoinLayoutVisitor.Apply,
        SqlSubqueryLayoutVisitor.Apply,
        SqlCaseLayoutVisitor.Apply,
        SqlClauseLayoutVisitor.Apply,
        SqlWhereConnectorLayout.Apply,
    ];

    /// <param name="formattedBatch">The batch as the script generator wrote it.</param>
    /// <param name="originalBatch">The batch as it was before formatting, used to keep blank lines.</param>
    internal static string Apply(string formattedBatch, string originalBatch)
    {
        var current = SqlLayoutDocument.Parse(formattedBatch.Replace("\r\n", "\n"));

        if (current is null)
        {
            return formattedBatch;
        }

        var original = SqlLayoutDocument.Parse(originalBatch.Replace("\r\n", "\n"));

        if (original is not null)
        {
            current = Run(current, document => SqlStatementSpacing.RepairSemicolons(document, original));
        }

        foreach (var phase in LayoutPhases)
        {
            current = Run(current, phase);
        }

        if (original is not null)
        {
            current = Run(current, document => SqlStatementSpacing.Space(document, original));
        }

        return Run(current, SqlWhitespaceCleanup.Apply).Text;
    }

    /// <summary>Applies one phase; the result is kept only when it differs and still parses.</summary>
    private static SqlLayoutDocument Run(SqlLayoutDocument current, Func<SqlLayoutDocument, string?> phase)
    {
        var candidateText = phase(current);

        if (candidateText is null || string.Equals(candidateText, current.Text, StringComparison.Ordinal))
        {
            return current;
        }

        return SqlLayoutDocument.Parse(candidateText) ?? current;
    }
}
