using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Core;

/// <summary>Formats parseable T-SQL batches without writing to a file.</summary>
public static partial class SqlFormatter
{
    public static SqlFormatResult Format(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (HasUnsupportedGoRepeatCount(text))
        {
            return Failure(text, "GO repeat counts are not supported by the safe formatter preview.");
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        SqlScriptGeneratorOptions generatorOptions = new()
        {
            PreserveComments = true,
        };
        Sql160ScriptGenerator generator = new(generatorOptions);
        List<string> formattedBatches = [];

        foreach (var batch in BatchSplitter.Split(text))
        {
            if (string.IsNullOrWhiteSpace(batch.Text))
            {
                continue;
            }

            var tokens = parser.GetTokenStream(
                new StringReader(batch.Text),
                out IList<ParseError> tokenErrors);
            if (tokenErrors.Count > 0)
            {
                return Failure(text, tokenErrors[0].Message);
            }

            var fragment = parser.Parse(tokens, out IList<ParseError> parseErrors);
            if (parseErrors.Count > 0 || fragment is null)
            {
                string message = parseErrors.Count > 0
                    ? parseErrors[0].Message
                    : "The SQL batch could not be parsed.";
                return Failure(text, message);
            }

            fragment.ScriptTokenStream = tokens;
            generator.GenerateScript(fragment, out var formattedBatch);
            formattedBatches.Add(formattedBatch.TrimEnd());
        }

        if (formattedBatches.Count == 0)
        {
            return new SqlFormatResult(true, text, []);
        }

        StringBuilder result = new();
        result.Append(string.Join("\nGO\n", formattedBatches));
        if (text.EndsWith('\n'))
        {
            result.Append('\n');
        }

        return new SqlFormatResult(true, result.ToString(), []);
    }

    private static SqlFormatResult Failure(string originalText, string message)
    {
        return new SqlFormatResult(false, originalText, [message]);
    }

    private static bool HasUnsupportedGoRepeatCount(string text)
    {
        return GoRepeatCountPattern().IsMatch(text);
    }

    [GeneratedRegex("(?im)^\\s*GO\\s+[0-9]+\\s*$")]
    private static partial Regex GoRepeatCountPattern();
}

public sealed record SqlFormatResult(
    bool IsSuccess,
    string Text,
    IReadOnlyList<string> Errors
);
