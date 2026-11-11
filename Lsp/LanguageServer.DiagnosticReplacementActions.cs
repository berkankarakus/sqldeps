using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

/// <summary>Builds diagnostic-specific replacement actions and their safe edit ranges.</summary>
public sealed partial class LanguageServer
{
    private object? ImplicitFilterConversionAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "implicit-filter-conversion")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var literal = TextInRange(text, diagnostic.Range);
        var match = ImplicitFilterConversionPattern().Match(diagnostic.Message);

        if (literal is null)
        {
            return null;
        }

        if (!match.Success)
        {
            return null;
        }

        var type = match.Groups["type"].Value.Trim();
        var replacement = $"CAST({literal} AS {type})";

        return QuickFix(
            "Cast filter literal to column type",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected literal must be within the diagnostic range and the target type must be known.");
    }

    private object? PagingLiteralAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (diagnostic.Rule is not ("negative-offset" or "invalid-fetch"))
        {
            return null;
        }

        var literal = TextInRange(text, diagnostic.Range);
        if (!int.TryParse(literal, out var value))
        {
            return null;
        }

        if (IsInvalidPagingValue(diagnostic.Rule, value))
        {
            return null;
        }

        var replacement = diagnostic.Rule == "negative-offset" ? "0" : "1";
        var title = diagnostic.Rule == "negative-offset"
            ? "Set OFFSET to 0"
            : "Set FETCH count to 1";

        return QuickFix(
            title,
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The paging literal must be a valid numeric value in the current document range.");
    }

    private static bool IsInvalidPagingValue(string rule, int value)
    {
        if (rule == "negative-offset")
        {
            return value >= 0;
        }

        return rule == "invalid-fetch" && value > 0;
    }

    private object? MissingProcedureParameterAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (diagnostic.Rule != "missing-procedure-parameter")
        {
            return null;
        }

        var call = TextInRange(text, diagnostic.Range);
        var match = RequiredProcedureParameterPattern().Match(diagnostic.Message);
        if (call is null)
        {
            return null;
        }

        if (!match.Success)
        {
            return null;
        }

        var missingParameter = TryGetMissingParameter(diagnostic);
        if (missingParameter is not null && IsOutputParameter(missingParameter))
        {
            return null;
        }

        if (!TryGetProcedureCallInsertion(text, diagnostic.Range, out var insertionRange, out var callBody))
        {
            return null;
        }

        var hasArgument = callBody.Count(character => character == ' ') >= 2;
        var separator = hasArgument ? ", " : " ";
        var parameter = match.Groups["parameter"].Value;

        return QuickFix(
            $"Add required parameter '{parameter}' placeholder",
            uri,
            insertionRange,
            $"{separator}{parameter} = NULL",
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The procedure and missing parameter must resolve uniquely in the index.");
    }

    private bool IsOutputParameter(MissingProcedureParameter parameter)
    {
        return AllParameters().Any(candidate => IsMatchingOutputParameter(candidate, parameter));
    }

    private static bool IsMatchingOutputParameter(
        ObjectParameter candidate,
        MissingProcedureParameter parameter)
    {
        if (!SameName(candidate.ObjectName, parameter.Procedure))
        {
            return false;
        }

        if (!string.Equals(candidate.Name, parameter.Parameter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.IsOutput;
    }

    private object? WhereTemplateAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("update-without-where" or "delete-without-where"))
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        if (PrimaryKeyFilterAction(uri, text, diagnostic) is { } primaryKeyAction)
        {
            return primaryKeyAction;
        }

        if (!TryGetUnfilteredDmlInsertion(uri, text, diagnostic, out var insertionRange))
        {
            return null;
        }

        return QuickFix(
            "Insert safe WHERE 1 = 0 template",
            uri,
            insertionRange,
            " WHERE 1 = 0",
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The target statement must be an UPDATE or DELETE without an existing WHERE clause.");
    }

    private static bool TryGetUnfilteredDmlInsertion(
        string uri,
        string text,
        LintDiagnostic diagnostic,
        out LspRange insertionRange)
    {
        insertionRange = default!;

        if (!IsDocumentRange(text, diagnostic.Range))
        {
            return false;
        }

        var analysis = AnalyzeDocument(uri, text);
        var targetOffset = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);

        foreach (var batch in analysis.ParsedBatches)
        {
            var batchOffset = OffsetAt(text, batch.StartLine - 1, 0);

            foreach (var statement in batch.Script.Batches.SelectMany(item => item.Statements))
            {
                if (!IsUnfilteredDml(statement, diagnostic.Rule))
                {
                    continue;
                }

                var statementStart = batchOffset + statement.StartOffset;
                var statementEnd = statementStart + statement.FragmentLength;

                if (targetOffset < statementStart || targetOffset > statementEnd)
                {
                    continue;
                }

                insertionRange = DmlInsertionRange(text, diagnostic.Range.End);

                return IsDocumentRange(text, insertionRange);
            }
        }

        return false;
    }

    private static bool IsUnfilteredDml(TSqlStatement statement, string rule)
    {
        if (rule == "update-without-where" && statement is UpdateStatement update)
        {
            return update.UpdateSpecification.WhereClause is null;
        }

        if (rule == "delete-without-where" && statement is DeleteStatement delete)
        {
            return delete.DeleteSpecification.WhereClause is null;
        }

        return false;
    }

    private static LspRange DmlInsertionRange(string text, LspPosition end)
    {
        var insertionPoint = end;
        var lines = text.Split('\n');

        if (insertionPoint.Line < 0 || insertionPoint.Line >= lines.Length)
        {
            return new(insertionPoint, insertionPoint);
        }

        var line = lines[insertionPoint.Line].TrimEnd('\r');
        var character = Math.Min(insertionPoint.Character, line.Length);

        while (character > 0 && char.IsWhiteSpace(line[character - 1]))
        {
            character--;
        }

        if (character > 0 && line[character - 1] == ';')
        {
            insertionPoint = new(insertionPoint.Line, character - 1);
        }

        return new(insertionPoint, insertionPoint);
    }

    private object? SimilarColumnAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (!IsUnknownColumnDiagnostic(diagnostic) || text is null)
        {
            return null;
        }

        var suggestion = SuggestedColumn(diagnostic.Message);

        if (suggestion is null)
        {
            return null;
        }

        var reference = TextInRange(text, diagnostic.Range);

        if (reference is null)
        {
            return null;
        }

        var replacement = ReplaceLastIdentifier(reference, suggestion);

        return QuickFix(
            $"Replace with '{replacement}'",
            uri,
            diagnostic.Range,
            replacement,
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The replacement must match a known column in the resolved table scope.");
    }

    private static bool IsUnknownColumnDiagnostic(LintDiagnostic diagnostic)
    {
        return diagnostic.Rule is "unknown-column" or "unknown-insert-column" or "unknown-update-column";
    }

    private static string? SuggestedColumn(string message)
    {
        var match = SuggestedColumnPattern().Match(message);

        return match.Success ? match.Groups["suggestion"].Value : null;
    }

    private static string ReplaceLastIdentifier(string reference, string replacement)
    {
        var separator = reference.LastIndexOf('.');

        return separator < 0 ? replacement : reference[..(separator + 1)] + replacement;
    }
}
