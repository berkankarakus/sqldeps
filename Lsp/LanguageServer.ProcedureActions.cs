using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddCompleteProcedureCallActions(
        List<object> actions,
        string uri,
        string? text,
        IReadOnlyList<LintDiagnostic> diagnostics)
    {
        if (text is null)
        {
            return;
        }

        var groups = diagnostics
            .Where(diagnostic => diagnostic.Rule == "missing-procedure-parameter")
            .Select(TryGetMissingParameter)
            .OfType<MissingProcedureParameter>()
            .GroupBy(item => (item.Procedure, item.Range));
        foreach (var group in groups)
        {
            if (group.Select(item => item.Parameter).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                continue;
            }

            if (CompleteProcedureCallAction(uri, text, group.ToArray()) is { } action)
            {
                actions.Add(action);
            }
        }
    }

    private object? CompleteProcedureCallAction(
        string uri,
        string text,
        IReadOnlyList<MissingProcedureParameter> missing)
    {
        var first = missing[0];
        var parameters = AllParameters()
            .Where(parameter => SameName(parameter.ObjectName, first.Procedure))
            .ToDictionary(parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);

        var required = missing
            .Select(item => parameters.GetValueOrDefault(item.Parameter))
            .ToArray();

        if (required.Any(parameter => parameter is null || parameter.IsOutput))
        {
            return null;
        }

        if (!TryGetProcedureCallInsertion(text, first.Range, out var insertionRange, out var callBody))
        {
            return null;
        }

        var separator = callBody.Count(character => character == ' ') >= 2 ? ", " : " ";
        var placeholders = required
            .OfType<ObjectParameter>()
            .OrderBy(parameter => parameter.Ordinal)
            .Select(ProcedureParameterPlaceholder)
            .ToArray();

        var action = QuickFix(
            "Complete procedure call with missing parameter placeholders",
            uri,
            insertionRange,
            separator + string.Join(", ", placeholders),
            text,
            first.Confidence);

        return action;
    }

    private static string ProcedureParameterPlaceholder(ObjectParameter parameter)
    {
        return $"{parameter.Name} = NULL";
    }

    private static bool TryGetProcedureCallInsertion(
        string text,
        LspRange diagnosticRange,
        out LspRange insertionRange,
        out string callBody)
    {
        insertionRange = default!;
        callBody = "";
        var lines = text.Split('\n');
        if (diagnosticRange.End.Line < 0 || diagnosticRange.End.Line >= lines.Length)
        {
            return false;
        }

        var line = diagnosticRange.End.Line;
        var lineText = lines[line].TrimEnd('\r');
        var start = Math.Min(diagnosticRange.Start.Character, lineText.Length);
        var end = Math.Min(diagnosticRange.End.Character, lineText.Length);
        while (end > diagnosticRange.Start.Character && char.IsWhiteSpace(lineText[end - 1]))
        {
            end--;
        }

        if (end > 0 && lineText[end - 1] == ';')
        {
            end--;
        }

        var statementEnd = lineText.IndexOf(';', end);
        if (statementEnd >= 0)
        {
            end = statementEnd;
        }

        callBody = lineText[start..end].Trim();
        if (callBody.Length == 0)
        {
            return false;
        }

        insertionRange = new LspRange(new(line, end), new(line, end));

        return true;
    }

    private static MissingProcedureParameter? TryGetMissingParameter(LintDiagnostic diagnostic)
    {
        var match = MissingProcedureParameterPattern().Match(diagnostic.Message);
        if (!match.Success)
        {
            return null;
        }

        return new MissingProcedureParameter(
            match.Groups["procedure"].Value,
            match.Groups["parameter"].Value,
            diagnostic.Range,
            diagnostic.Confidence);
    }

    private sealed record MissingProcedureParameter(
        string Procedure,
        string Parameter,
        LspRange Range,
        string Confidence);

    [GeneratedRegex(@"Procedure '(?<procedure>[^']+)' requires parameter '(?<parameter>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex MissingProcedureParameterPattern();
}
