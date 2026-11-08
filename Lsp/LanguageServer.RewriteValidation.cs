using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static bool TryValidateRewrite(
        string text,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        int changedLine,
        out string editedText)
    {
        editedText = "";

        if (!TryOrderRewriteEdits(text, edits, out var ordered))
        {
            return false;
        }

        if (!EditsStayInOneBatch(text, edits, changedLine))
        {
            return false;
        }

        editedText = ApplyRewriteEdits(text, ordered);
        var beforeProblems = ParseProblemsForBatch(text, changedLine);
        var afterProblems = ParseProblemsForBatch(editedText, changedLine);

        return afterProblems <= beforeProblems;
    }

    private static bool TryOrderRewriteEdits(
        string text,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        out (LspRange Range, string NewText, int Start, int End)[] ordered)
    {
        ordered = [];

        if (edits.Count == 0)
        {
            return false;
        }

        var resolved = new List<(LspRange Range, string NewText, int Start, int End)>();
        foreach (var edit in edits)
        {
            if (!TryGetOffsets(text, edit.Range, out var start, out var end))
            {
                return false;
            }

            resolved.Add((edit.Range, edit.NewText, start, end));
        }

        ordered = resolved
            .OrderBy(edit => edit.Start)
            .ToArray();

        if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
        {
            ordered = [];

            return false;
        }

        Array.Reverse(ordered);

        return true;
    }

    private static bool EditsStayInOneBatch(
        string text,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        int changedLine)
    {
        var batches = BatchSplitter.Split(text);
        var targetBatch = BatchIndexAtLine(batches, changedLine);
        if (targetBatch < 0)
        {
            return false;
        }

        return edits.All(edit =>
            BatchIndexAtLine(batches, edit.Range.Start.Line) == targetBatch
            && BatchIndexAtLine(batches, edit.Range.End.Line) == targetBatch);
    }

    private static int BatchIndexAtLine(IReadOnlyList<SqlBatch> batches, int zeroBasedLine)
    {
        var oneBasedLine = zeroBasedLine + 1;
        for (var index = 0; index < batches.Count; index++)
        {
            var batch = batches[index];
            var lastLine = batch.StartLine + batch.Text.Count(character => character == '\n');
            if (oneBasedLine >= batch.StartLine && oneBasedLine <= lastLine)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetOffsets(string text, LspRange range, out int start, out int end)
    {
        start = 0;
        end = 0;

        if (!IsValidDocumentPosition(text, range.Start))
        {
            return false;
        }

        if (!IsValidDocumentPosition(text, range.End))
        {
            return false;
        }

        start = OffsetAtPosition(text, range.Start);
        end = OffsetAtPosition(text, range.End);

        return end >= start;
    }

    private static bool IsValidDocumentPosition(string text, LspPosition position)
    {
        if (position.Line < 0 || position.Character < 0)
        {
            return false;
        }

        var lines = text.Split('\n');
        if (position.Line >= lines.Length)
        {
            return false;
        }

        var lineLength = lines[position.Line].TrimEnd('\r').Length;

        return position.Character <= lineLength;
    }

    private static int OffsetAtPosition(string text, LspPosition position)
    {
        var offset = 0;
        var line = 0;
        while (line < position.Line)
        {
            var lineEnd = text.IndexOf('\n', offset);
            if (lineEnd < 0)
            {
                return text.Length;
            }

            offset = lineEnd + 1;
            line++;
        }

        return offset + position.Character;
    }

    private static string ApplyRewriteEdits(
        string text,
        IEnumerable<(LspRange Range, string NewText, int Start, int End)> ordered)
    {
        var editedText = text;

        foreach (var edit in ordered)
        {
            editedText = editedText[..edit.Start] + edit.NewText + editedText[edit.End..];
        }

        return editedText;
    }

    private static int ParseProblemsForBatch(string text, int zeroBasedLine)
    {
        var batches = BatchSplitter.Split(text);
        var batchIndex = BatchIndexAtLine(batches, zeroBasedLine);
        if (batchIndex < 0)
        {
            return 0;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(batches[batchIndex].Text);
        parser.Parse(reader, out IList<ParseError> errors);

        return errors.Count;
    }
}

