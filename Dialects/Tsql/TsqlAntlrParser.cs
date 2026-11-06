using Antlr4.Runtime;
using SqlDeps;
using SqlDeps.Dialects;
using SqlDeps.Dialects.Tsql.Generated;

namespace SqlDeps.Dialects.Tsql;

/// <summary>Parses a deliberately small T-SQL subset for ScriptDom comparison experiments.</summary>
public sealed class TsqlAntlrParser
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

        return new SqlDialectParseResult(errors.Count == 0, statements, errors);
    }

    private static void ParseBatch(
        SqlBatch batch,
        List<SqlDialectStatement> statements,
        List<SqlDialectParseError> errors
    )
    {
        AntlrInputStream input = new(batch.Text);
        TsqlSubsetLexer lexer = new(input);
        CollectingErrorListener errorListener = new(batch.StartLine - 1);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        CommonTokenStream tokens = new(lexer);
        TsqlSubsetParser parser = new(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(errorListener);

        var tree = parser.script();
        errors.AddRange(errorListener.Errors);
        if (errorListener.Errors.Count > 0)
        {
            return;
        }

        statements.AddRange(
            tree.statement().Select(statement => CreateStatement(statement, batch.StartLine - 1))
        );
    }

    private static SqlDialectStatement CreateStatement(
        TsqlSubsetParser.StatementContext statement,
        int lineOffset
    )
    {
        if (statement.selectStatement() is { } select)
        {
            return new SqlDialectStatement(
                "select",
                CreateSelectReferences(select, lineOffset));
        }

        var createTable = statement.createTableStatement()!;
        return new SqlDialectStatement(
            "create-table",
            [CreateReference(
                createTable.qualifiedName(),
                SqlDialectReferenceKind.Table,
                lineOffset
            )]
        );
    }

    private static IReadOnlyList<SqlDialectReference> CreateSelectReferences(
        TsqlSubsetParser.SelectStatementContext statement,
        int lineOffset
    )
    {
        var references = new List<SqlDialectReference>
        {
            CreateReference(
                statement.qualifiedName(),
                SqlDialectReferenceKind.Table,
                lineOffset
            ),
        };

        foreach (var item in statement.selectList().selectItem())
        {
            references.Add(
                CreateReference(
                    item.qualifiedName(),
                    SqlDialectReferenceKind.Column,
                    lineOffset
                )
            );
        }

        return references;
    }

    private static SqlDialectReference CreateReference(
        TsqlSubsetParser.QualifiedNameContext name,
        SqlDialectReferenceKind kind,
        int lineOffset
    )
    {
        var text = name.GetText();
        var start = name.Start;
        var stop = name.Stop;

        if (stop is null)
        {
            stop = start;
        }

        var tokenLength = stop.Text is null ? 1 : stop.Text.Length;
        var endCharacter = stop.Column + tokenLength;

        return new SqlDialectReference(
            text,
            kind,
            new SqlDialectRange(
                lineOffset + start.Line - 1,
                start.Column,
                lineOffset + stop.Line - 1,
                endCharacter));
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
            string msg,
            RecognitionException e
        )
        {
            var offendingText = offendingSymbol is null ? null : offendingSymbol.Text;
            var tokenLength = offendingText is null ? 1 : offendingText.Length;
            AddError(msg, line, charPositionInLine, tokenLength);
        }

        public void SyntaxError(
            TextWriter output,
            IRecognizer recognizer,
            int offendingSymbol,
            int line,
            int charPositionInLine,
            string msg,
            RecognitionException e
        )
        {
            AddError(msg, line, charPositionInLine, 1);
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
