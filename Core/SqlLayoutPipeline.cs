namespace SqlDeps.Core;

/// <summary>
/// Runs the layout phases over one formatted batch. Every phase reads a fresh parse of the previous result and a
/// phase whose output no longer parses is skipped, so the batch always stays valid T-SQL.
/// </summary>
internal static class SqlLayoutPipeline
{
    private static readonly Func<SqlLayoutDocument, string?>[] Phases =
    [
        SqlStatementLayoutVisitor.Apply,
        SqlSingleSelectLayout.Apply,
        SqlJoinLayoutVisitor.Apply,
        SqlSubqueryLayoutVisitor.Apply,
        SqlClauseLayoutVisitor.Apply,
        SqlWhereConnectorLayout.Apply,
        SqlWhitespaceCleanup.Apply,
    ];

    public static string Apply(string formattedBatch)
    {
        var current = SqlLayoutDocument.Parse(formattedBatch.Replace("\r\n", "\n"));

        if (current is null)
        {
            return formattedBatch;
        }

        foreach (var phase in Phases)
        {
            var candidateText = phase(current);

            if (candidateText is null || string.Equals(candidateText, current.Text, StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = SqlLayoutDocument.Parse(candidateText);

            if (candidate is not null)
            {
                current = candidate;
            }
        }

        return current.Text;
    }
}
