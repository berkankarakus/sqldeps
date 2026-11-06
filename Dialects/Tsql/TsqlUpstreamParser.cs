using Antlr4.Runtime;
using SqlDeps;
using SqlDeps.Dialects;
using SqlDeps.Dialects.Tsql.Upstream.Generated;

namespace SqlDeps.Dialects.Tsql;

/// <summary>Parses T-SQL with the vendored grammars-v4 parser without extracting semantic references yet.</summary>
public sealed class TsqlUpstreamParser
{
    public SqlDialectParseResult Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        List<SqlDialectParseError> errors = [];

        foreach (var batch in BatchSplitter.Split(text))
        {
            if (string.IsNullOrWhiteSpace(batch.Text))
            {
                continue;
            }

            ParseBatch(batch, errors);
        }

        return new SqlDialectParseResult(
            errors.Count == 0,
            [],
            errors);
    }

    private static void ParseBatch(
        SqlBatch batch,
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
        parser.tsql_file();

        errors.AddRange(errorListener.Errors);
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
