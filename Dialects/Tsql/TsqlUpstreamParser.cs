using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using SqlDeps;
using SqlDeps.Dialects;
using SqlDeps.Dialects.Tsql.Upstream.Generated;

namespace SqlDeps.Dialects.Tsql;

/// <summary>Parses common T-SQL and maps table and column references to the shared dialect model.</summary>
public sealed class TsqlUpstreamParser
{
    public SqlDialectParseResult Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        List<SqlDialectStatement> statements = [];
        List<SqlDialectParseError> errors = [];

        foreach (var batch in BatchSplitter.Split(text))
        {
            if (string.IsNullOrWhiteSpace(batch.Text))
            {
                continue;
            }

            ParseBatch(batch, statements, errors);
        }

        return new SqlDialectParseResult(
            errors.Count == 0,
            statements,
            errors);
    }

    private static void ParseBatch(
        SqlBatch batch,
        List<SqlDialectStatement> statements,
        List<SqlDialectParseError> errors
    )
    {
        AntlrInputStream input = new(batch.Text);
        TSqlLexer lexer = new(input);
        CollectingErrorListener errorListener = new(batch.StartLine - 1);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);

        CommonTokenStream tokens = new(lexer);
        TSqlParser parser = new(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);
        var tree = parser.tsql_file();

        errors.AddRange(errorListener.Errors);
        if (errorListener.Errors.Count > 0)
        {
            return;
        }

        statements.AddRange(CreateStatements(tree, batch.StartLine - 1));
    }

    private static IReadOnlyList<SqlDialectStatement> CreateStatements(
        TSqlParser.Tsql_fileContext tree,
        int lineOffset
    )
    {
        List<SqlDialectStatement> statements = [];

        foreach (var query in Descendants<TSqlParser.Query_specificationContext>(tree))
        {
            statements.Add(CreateSelectStatement(query, lineOffset));
        }

        foreach (var createTable in Descendants<TSqlParser.Create_tableContext>(tree))
        {
            statements.Add(
                new SqlDialectStatement(
                    "create-table",
                    [CreateReference(
                        createTable.table_name(),
                        SqlDialectReferenceKind.Table,
                        lineOffset
                    )]
                )
            );
        }

        return statements;
    }

    private static SqlDialectStatement CreateSelectStatement(
        TSqlParser.Query_specificationContext query,
        int lineOffset
    )
    {
        List<SqlDialectReference> references = [];

        if (query.table_sources() is { } tableSources)
        {
            foreach (var table in Descendants<TSqlParser.Full_table_nameContext>(tableSources))
            {
                references.Add(
                    CreateReference(
                        table,
                        SqlDialectReferenceKind.Table,
                        lineOffset
                    )
                );
            }
        }

        foreach (var column in Descendants<TSqlParser.Full_column_nameContext>(query.select_list()))
        {
            references.Add(
                CreateReference(
                    column,
                    SqlDialectReferenceKind.Column,
                    lineOffset
                )
            );
        }

        return new SqlDialectStatement("select", references);
    }

    private static SqlDialectReference CreateReference(
        ParserRuleContext context,
        SqlDialectReferenceKind kind,
        int lineOffset
    )
    {
        var start = context.Start;
        var stop = context.Stop;

        if (stop is null)
        {
            stop = start;
        }

        var tokenLength = stop.Text is null ? 1 : stop.Text.Length;
        var endCharacter = stop.Column + tokenLength;

        return new SqlDialectReference(
            context.GetText(),
            kind,
            new SqlDialectRange(
                lineOffset + start.Line - 1,
                start.Column,
                lineOffset + stop.Line - 1,
                endCharacter
            )
        );
    }

    private static IEnumerable<T> Descendants<T>(IParseTree node)
        where T : class, IParseTree
    {
        if (node is T match)
        {
            yield return match;
        }

        for (var index = 0; index < node.ChildCount; index++)
        {
            foreach (var descendant in Descendants<T>(node.GetChild(index)))
            {
                yield return descendant;
            }
        }
    }

    private sealed class CollectingErrorListener(int lineOffset) :
        IAntlrErrorListener<IToken>,
        IAntlrErrorListener<int>
    {
        public List<SqlDialectParseError> Errors { get; } = [];

        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            IToken offendingSymbol,
            int line,
            int charPositionInLine,
            string message,
            RecognitionException exception
        )
        {
            var offendingText = offendingSymbol is null ? null : offendingSymbol.Text;
            var tokenLength = offendingText is null ? 1 : offendingText.Length;
            AddError(message, line, charPositionInLine, tokenLength);
        }

        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string message,
            RecognitionException exception
        )
        {
            AddError(message, line, charPositionInLine, 1);
        }

        private void AddError(string message, int line, int character, int tokenLength)
        {
            var endCharacter = character + Math.Max(1, tokenLength);
            Errors.Add(new SqlDialectParseError(
                message,
                new SqlDialectRange(
                    lineOffset + line - 1,
                    character,
                    lineOffset + line - 1,
                    endCharacter)));
        }
    }
}
