using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

/// <summary>Handles LSP quick-fix requests and diagnostic decoding.</summary>
public sealed partial class LanguageServer
{
    private object CodeActions(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var actions = new List<object>();
        refactorQueriesInitialized = false;
        refactorQueries = null;
        var document = OpenDocument(uri);
        var diagnostics = LintDiagnostics(parameters).Distinct().ToArray();

        AddExperimentalActions(actions, uri, document?.Text, diagnostics, parameters);
        AddRepositoryContextAction(actions, parameters, document?.Text);
        AddDependencyQueryActions(actions, parameters, document?.Text);
        AddDiagnosticActions(actions, uri, document?.Text, diagnostics);

        return FilterCodeActions(actions, parameters);
    }

    private void AddExperimentalActions(
        List<object> actions,
        string uri,
        string? text,
        IReadOnlyList<LintDiagnostic> diagnostics,
        JsonElement parameters)
    {
        if (!experimentalSuggestions)
        {
            return;
        }

        if (OrderByOrdinalAction(uri, text, parameters) is { } orderByAction)
        {
            actions.Add(orderByAction);
        }

        AddQueryBuilderAction(actions, parameters, text);
        AddCompleteProcedureCallActions(actions, uri, text, diagnostics);
    }

    private void AddDiagnosticActions(
        List<object> actions,
        string uri,
        string? text,
        IReadOnlyList<LintDiagnostic> diagnostics)
    {
        var reindexActionAdded = false;

        foreach (var diagnostic in diagnostics)
        {
            if (IsStaleDiagnostic(diagnostic))
            {
                if (!reindexActionAdded)
                {
                    actions.Add(ReindexAction());
                    reindexActionAdded = true;
                }

                continue;
            }

            AddSuppressionActions(actions, uri, text, diagnostic);
            AddReplacementActions(actions, uri, text, diagnostic);
        }
    }

    private static bool IsStaleDiagnostic(LintDiagnostic diagnostic)
    {
        return diagnostic.IsStale || diagnostic.Source == "stale-index";
    }

    private void AddSuppressionActions(
        List<object> actions,
        string uri,
        string? text,
        LintDiagnostic diagnostic)
    {
        if (SuppressLintAction(uri, text, diagnostic) is { } suppressionAction)
        {
            actions.Add(suppressionAction);
        }

        if (SuppressFileAction(uri, text, diagnostic) is { } fileSuppressionAction)
        {
            actions.Add(fileSuppressionAction);
        }

        if (SuppressRepositoryAction(diagnostic) is { } repositorySuppressionAction)
        {
            actions.Add(repositorySuppressionAction);
        }
    }

    private object ReindexAction() => new
    {
        title = "Reindex workspace (sqldeps reindex)",
        kind = "source",
        data = ReindexMetadata(),
        command = new
        {
            title = "sqldeps reindex",
            command = "sqldeps.reindex",
            arguments = new[] { workspaceRoot },
        },
    };

    private static object ReindexMetadata() => new
    {
        source = "stale-index",
        confidence = "low",
        stale = true,
        writesFile = false,
        applicability = "The workspace index must be refreshed before stale diagnostics can be trusted.",
    };

    private static bool IsDocumentRange(string text, LspRange range)
    {
        if (HasInvalidRangeOrder(range))
        {
            return false;
        }

        var lines = text.Split('\n');

        if (IsOutsideDocument(lines, range))
        {
            return false;
        }

        var startLineLength = lines[range.Start.Line].TrimEnd('\r').Length;
        var endLineLength = lines[range.End.Line].TrimEnd('\r').Length;

        return range.Start.Character <= startLineLength && range.End.Character <= endLineLength;
    }

    private static bool HasInvalidRangeOrder(LspRange range)
    {
        if (range.Start.Line < 0)
        {
            return true;
        }

        if (range.Start.Character < 0)
        {
            return true;
        }

        if (range.End.Line < range.Start.Line)
        {
            return true;
        }

        return range.End.Line == range.Start.Line && range.End.Character < range.Start.Character;
    }

    private static bool IsOutsideDocument(string[] lines, LspRange range)
    {
        return range.Start.Line >= lines.Length || range.End.Line >= lines.Length;
    }

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

    private object? SelectStarAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "select-star")
        {
            return null;
        }

        if (text is null)
        {
            return null;
        }

        var match = SelectStarPattern().Match(text);
        var star = match.Groups["star"];

        if (!match.Success)
        {
            return SelectStarActionFromParsedQuery(uri, text, diagnostic);
        }

        if (!star.Success)
        {
            return null;
        }

        int starOffset = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);

        if (star.Index != starOffset)
        {
            return null;
        }

        if (HasJoinOrApply(text, match.Index + match.Length))
        {
            return null;
        }

        string tableName = SelectStarTableName(match.Groups["table"].Value);
        string[] columns = SelectStarColumns(tableName, uri, text, diagnostic.Range.Start.Line);

        if (columns.Length == 0)
        {
            return SelectStarActionFromParsedQuery(uri, text, diagnostic);
        }

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            diagnostic.Range,
            string.Join(", ", columns),
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected star must resolve to one table with known column metadata.");
    }

    private object? SelectStarActionFromParsedQuery(string uri, string text, LintDiagnostic diagnostic)
    {
        if (!TryFindSingleSourceName(uri, text, diagnostic, out var tableName))
        {
            return null;
        }

        string[] columns = SelectStarColumns(tableName, uri, text, diagnostic.Range.Start.Line);

        if (columns.Length == 0)
        {
            return null;
        }

        return SelectStarQuickFix(
            "Expand SELECT * with indexed columns",
            uri,
            diagnostic.Range,
            string.Join(", ", columns),
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected star must resolve to one table with known column metadata.");
    }

    private object? SelectStarQuickFix(
        string title,
        string uri,
        LspRange range,
        string replacement,
        string text,
        string confidence,
        IReadOnlyList<DiagnosticReference> references,
        bool experimental,
        string diagnosticRule,
        string applicability)
    {
        return QuickFix(
            title,
            uri,
            range,
            replacement,
            text,
            confidence,
            references,
            experimental,
            diagnosticRule,
            applicability);
    }

    private static bool TryFindSingleSourceName(
        string uri,
        string text,
        LintDiagnostic diagnostic,
        out string tableName)
    {
        tableName = "";

        if (diagnostic.Rule != "select-star" || TextInRange(text, diagnostic.Range) != "*")
        {
            return false;
        }

        AnalysisResult analysis = DocumentAnalysis.Analyze(uri, text, ObjectNames.StandardDefaultSchema);
        var candidates = analysis.ParsedBatches
            .SelectMany(batch => SelectStarQueries(batch.Script)
                .Select(star => (batch, star.Query, star.Star)))
            .Where(candidate =>
            {
                int starOffset = BatchOffset(text, candidate.batch.StartLine) + candidate.Star.StartOffset;
                LspRange starRange = PositionAt(text, starOffset);

                return starRange.Start.Line == diagnostic.Range.Start.Line
                    && Math.Abs(starRange.Start.Character - diagnostic.Range.Start.Character) <= 2;
            })
            .ToArray();

        if (candidates.Length != 1)
        {
            return false;
        }

        var sources = candidates[0].Query.FromClause?.TableReferences;

        if (sources is null || sources.Count != 1)
        {
            return false;
        }

        return TryGetSingleSourceName(sources[0], out tableName);
    }

    private static bool TryGetSingleSourceName(TableReference source, out string tableName)
    {
        tableName = source switch
        {
            NamedTableReference table when table.SchemaObject.SchemaIdentifier is null => table.SchemaObject.BaseIdentifier.Value,
            NamedTableReference table => ObjectNames.Format(table.SchemaObject, ObjectNames.StandardDefaultSchema),
            VariableTableReference variable => variable.Variable.Name,
            QueryDerivedTable derived when derived.Alias is not null => derived.Alias.Value,
            _ => "",
        };

        return tableName.Length > 0;
    }

    private static bool HasJoinOrApply(string text, int searchStart)
    {
        int statementEnd = text.IndexOf(';', searchStart);
        int end = statementEnd < 0 ? text.Length : statementEnd;
        string statementTail = text[searchStart..end];

        return Regex.IsMatch(statementTail, @"\b(?:JOIN|APPLY)\b", RegexOptions.IgnoreCase);
    }

    private static string SelectStarTableName(string rawTableName)
    {
        string tableName = rawTableName.Replace("[", "").Replace("]", "");

        return IsLocalTableName(tableName) ? tableName : Qualify(tableName);
    }

    private string[] SelectStarColumns(string tableName, string uri, string text, int zeroBasedLine)
    {
        if (!IsLocalTableName(tableName))
        {
            string[] indexedColumns = IndexedColumnsFor(tableName);

            if (indexedColumns.Length > 0)
            {
                return indexedColumns;
            }
        }

        AnalysisResult analysis = AnalyzeDocument(uri, text);
        int targetLine = zeroBasedLine + 1;
        var localColumns = analysis.LocalColumns
            .Where(column => string.Equals(column.Alias, tableName, StringComparison.OrdinalIgnoreCase))
            .Where(column => column.Line <= targetLine)
            .ToArray();
        int latestDropLine = localColumns
            .Where(column => column.IsDrop)
            .Select(column => column.Line)
            .DefaultIfEmpty(0)
            .Max();
        var scopes = localColumns
            .Where(column => !column.IsDrop && column.Line > latestDropLine)
            .GroupBy(column => column.ScopeLine)
            .ToArray();

        if (scopes.Length != 1)
        {
            return [];
        }

        return scopes[0]
            .Select(column => column.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLocalTableName(string tableName)
    {
        if (tableName.Length == 0)
        {
            return false;
        }

        return tableName[0] == '@' || tableName[0] == '#';
    }

    private string[] IndexedColumnsFor(string tableName)
    {
        return AllTableColumns()
            .Where(column => string.Equals(column.TableName, tableName, StringComparison.OrdinalIgnoreCase))
            .Select(column => column.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
