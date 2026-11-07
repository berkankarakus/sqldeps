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
        var reindexActionAdded = false;

        if (experimentalSuggestions
            && OrderByOrdinalAction(uri, document?.Text, parameters) is { } orderByAction)
        {
            actions.Add(orderByAction);
        }

        if (experimentalSuggestions)
        {
            AddQueryBuilderAction(actions, parameters, document?.Text);
            AddCompleteProcedureCallActions(actions, uri, document?.Text, diagnostics);
        }

        AddRepositoryContextAction(actions, parameters, document?.Text);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.IsStale || diagnostic.Source == "stale-index")
            {
                if (!reindexActionAdded)
                {
                    actions.Add(ReindexAction());
                    reindexActionAdded = true;
                }

                continue;
            }

            if (SuppressLintAction(uri, document?.Text, diagnostic) is { } suppressionAction)
            {
                actions.Add(suppressionAction);
            }

            if (SuppressFileAction(uri, document?.Text, diagnostic) is { } fileSuppressionAction)
            {
                actions.Add(fileSuppressionAction);
            }

            AddReplacementActions(actions, uri, document?.Text, diagnostic);
        }

        return FilterCodeActions(actions, parameters);
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
        if (diagnostic.Rule != "implicit-filter-conversion" || text is null)
        {
            return null;
        }

        var literal = TextInRange(text, diagnostic.Range);
        var match = ImplicitFilterConversionPattern().Match(diagnostic.Message);

        if (literal is null || !match.Success)
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
        if (text is null || diagnostic.Rule is not ("negative-offset" or "invalid-fetch"))
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
        if (text is null || diagnostic.Rule != "missing-procedure-parameter")
        {
            return null;
        }

        var call = TextInRange(text, diagnostic.Range);
        var match = RequiredProcedureParameterPattern().Match(diagnostic.Message);
        if (call is null || !match.Success)
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
        return AllParameters().Any(candidate => SameName(candidate.ObjectName, parameter.Procedure)
            && string.Equals(candidate.Name, parameter.Parameter, StringComparison.OrdinalIgnoreCase)
            && candidate.IsOutput);
    }

    private object? WhereTemplateAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule is not ("update-without-where" or "delete-without-where"))
        {
            return null;
        }

        var insertionPoint = diagnostic.Range.End;
        if (text is not null && insertionPoint.Line >= 0)
        {
            var lines = text.Split('\n');
            if (insertionPoint.Line < lines.Length)
            {
                var line = lines[insertionPoint.Line].TrimEnd('\r');
                var end = Math.Min(insertionPoint.Character, line.Length);
                while (end > 0 && char.IsWhiteSpace(line[end - 1]))
                {
                    end--;
                }

                if (end > 0 && line[end - 1] == ';')
                {
                    insertionPoint = new(insertionPoint.Line, end - 1);
                }
            }
        }

        var insertionRange = new LspRange(insertionPoint, insertionPoint);

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

    private object? QuickFixEdits(
        string title,
        string uri,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        string? text,
        string diagnosticConfidence,
        IReadOnlyList<DiagnosticReference>? references = null,
        bool experimental = false,
        string? diagnosticRule = null,
        string? applicability = null,
        bool includeRefactorCandidate = true
    )
    {
        if (!AreQuickFixEditsValid(text, edits))
        {
            return null;
        }

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(
                title,
                uri,
                diagnosticConfidence,
                edits,
                references,
                experimental,
                diagnosticRule,
                applicability,
                includeRefactorCandidate),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [uri] = edits.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray(),
                },
            },
        };
    }

    private static bool AreQuickFixEditsValid(
        string? text,
        IReadOnlyList<(LspRange Range, string NewText)> edits)
    {
        if (text is null)
        {
            return false;
        }

        if (edits.Count == 0)
        {
            return false;
        }

        return TryValidateRewrite(text, edits, edits[0].Range.Start.Line, out _);
    }

    private object ReplacementActionData(
        string title,
        string uri,
        string diagnosticConfidence,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        IReadOnlyList<DiagnosticReference>? references = null,
        bool experimental = false,
        string? diagnosticRule = null,
        string? applicability = null,
        bool includeRefactorCandidate = true
    )
    {
        var metadata = DiagnosticMetadataMapper.For("sqldeps");
        IReadOnlyList<DiagnosticReference> actionReferences = references is null ? [] : references;
        RefactorCandidate? refactorCandidate = !includeRefactorCandidate || diagnosticRule is null
            ? null
            : BuildRefactorCandidate(
                title,
                uri,
                diagnosticRule,
                diagnosticConfidence,
                actionReferences,
                edits,
                applicability);

        return new
        {
            source = metadata.Source,
            confidence = metadata.Confidence,
            diagnosticConfidence,
            stale = metadata.IsStale,
            generation = "parse-validated-rewrite",
            writesFile = false,
            isRefactorCandidate = includeRefactorCandidate,
            experimental,
            rule = diagnosticRule,
            applicability,
            explanation = ExplanationMetadataFor(diagnosticRule),
            references = actionReferences,
            diff = edits.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray(),
            refactorCandidate,
        };
    }

    private RefactorCandidate? BuildRefactorCandidate(
        string title,
        string uri,
        string rule,
        string confidence,
        IReadOnlyList<DiagnosticReference> references,
        IReadOnlyList<(LspRange Range, string NewText)> edits,
        string? applicability)
    {
        var textEdits = edits
            .Select(edit => new RefactorTextEdit(
                new RefactorRange(
                    new RefactorPosition(edit.Range.Start.Line, edit.Range.Start.Character),
                    new RefactorPosition(edit.Range.End.Line, edit.Range.End.Character)),
                edit.NewText))
            .ToArray();
        var workspaceEdit = new RefactorWorkspaceEdit(
            new Dictionary<string, IReadOnlyList<RefactorTextEdit>>
            {
                [uri] = textEdits,
            });
        var impact = new RefactorDependencyImpact(
            "unavailable",
            "Dependency impact is not resolved in a standard LSP codeAction request.",
            []);

        var candidate = new RefactorCandidate(
            title,
            "quickfix",
            rule,
            confidence,
            applicability,
            references,
            impact,
            workspaceEdit,
            WritesFile: false);

        var document = OpenDocument(uri);

        if (document is null)
        {
            return candidate;
        }

        var queries = CreateRefactorImpactQueries();

        if (queries is null || !queries.IsIndexCurrent())
        {
            return null;
        }

        var enriched = RefactorCandidatePreview.AddDiagnosticContext(
            candidate,
            document,
            queries);

        if (!RefactorCandidatePreview.IsSafeCandidate(enriched))
        {
            return null;
        }

        var sourceFile = LspUri.PathFromUri(uri);

        if (!RefactorCandidatePreview.IsParseValidPreview(
                enriched,
                sourceFile,
                document.Text,
                ObjectNames.StandardDefaultSchema))
        {
            return null;
        }

        return enriched;
    }

    private IndexQueryService? CreateRefactorImpactQueries()
    {
        if (refactorQueriesInitialized)
        {
            return refactorQueries;
        }

        refactorQueriesInitialized = true;
        var root = workspaceAnalysisRoot;

        if (root is null)
        {
            return null;
        }

        var databasePath = WorkspaceIndexLocator.Find(
            root,
            selectedIndexPath,
            allowAutomaticIndexSelection);

        if (databasePath is null)
        {
            return null;
        }

        try
        {
            refactorQueries = new IndexQueryService(new IndexDatabase(databasePath));

            return refactorQueries;
        }

        catch (Exception exception)
        {
            Console.Error.WriteLine("sqldeps-lsp: refactor impact unavailable: " + exception.Message);

            return null;
        }
    }

    private static LspRange PositionAt(string text, int offset)
    {
        var bounded = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < bounded; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        var character = bounded - lineStart;
        return new(new(line, character), new(line, character));
    }

    private static IEnumerable<LintDiagnostic> LintDiagnostics(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("context", out var context))
        {
            return [];
        }

        if (!context.TryGetProperty("diagnostics", out var diagnostics))
        {
            return [];
        }

        return diagnostics.EnumerateArray().Select(TryLintDiagnostic).OfType<LintDiagnostic>();
    }

    private static LintDiagnostic? TryLintDiagnostic(JsonElement diagnostic)
    {
        if (!IsSqldepsDiagnostic(diagnostic))
        {
            return null;
        }

        if (!TryGetStringProperty(diagnostic, "code", out var rule))
        {
            return null;
        }

        if (!diagnostic.TryGetProperty("range", out var range))
        {
            return null;
        }

        if (!TryGetRange(range, out var diagnosticRange))
        {
            return null;
        }

        var message = diagnostic.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? ""
            : "";

        var source = diagnostic.TryGetProperty("data", out var data)
            && data.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString() ?? ""
            : "";
        var confidence = diagnostic.TryGetProperty("data", out data)
            && data.TryGetProperty("confidence", out var confidenceElement)
            ? confidenceElement.GetString() ?? "high"
            : "high";
        var stale = source == "stale-index"
            || diagnostic.TryGetProperty("data", out data)
            && data.TryGetProperty("stale", out var staleElement)
            && staleElement.ValueKind == JsonValueKind.True;

        return new LintDiagnostic(rule, diagnosticRange, message, source, confidence, stale);
    }

    private static bool IsSqldepsDiagnostic(JsonElement diagnostic)
    {
        if (!TryGetStringProperty(diagnostic, "source", out var source))
        {
            return false;
        }

        return string.Equals(source, "sqldeps", StringComparison.Ordinal);
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = "";

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";

        return true;
    }

    private static bool TryGetRange(JsonElement range, out LspRange result)
    {
        result = default!;

        if (!range.TryGetProperty("start", out var start))
        {
            return false;
        }

        if (!TryGetPosition(start, out var startPosition))
        {
            return false;
        }

        if (!range.TryGetProperty("end", out var end))
        {
            return false;
        }

        if (!TryGetPosition(end, out var endPosition))
        {
            return false;
        }

        result = new(startPosition, endPosition);

        return true;
    }

    private static bool TryGetPosition(JsonElement position, out LspPosition result)
    {
        result = default!;

        if (!TryGetInt32Property(position, "line", out var line))
        {
            return false;
        }

        if (!TryGetInt32Property(position, "character", out var character))
        {
            return false;
        }

        result = new(line, character);

        return true;
    }

    private static bool TryGetInt32Property(JsonElement element, string propertyName, out int value)
    {
        value = 0;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (!property.TryGetInt32(out value))
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"\bSELECT\s+(?<star>\*)\s+FROM\s+(?<table>(?:[@#]?(?:\[[^\]]+\]|\w+))(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?:\s+(?:AS\s+)?\w+)?(?=\s*(?:WHERE|GROUP|ORDER|JOIN|;|$))", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectStarPattern();

    [GeneratedRegex(@"String literal .* is compared with '(?<type>[^']+)' column", RegexOptions.IgnoreCase)]
    private static partial Regex ImplicitFilterConversionPattern();

    [GeneratedRegex(@"requires parameter '(?<parameter>@[A-Za-z0-9_]+)'", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredProcedureParameterPattern();

    [GeneratedRegex(@"INSERT\s+omits\s+required\s+column\s+'(?<column>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex MissingInsertColumnPattern();

    [GeneratedRegex(@"Invalid column name '(?<column>[^']+)' on '(?<table>[^']+)'\.", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownColumnPattern();

    [GeneratedRegex(@"\bWITH\s*\(\s*NOLOCK\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockOnlyHintPattern();

    [GeneratedRegex(@"\bWITH\s*\(\s*NOLOCK\s*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockFirstHintPattern();

    [GeneratedRegex(@",\s*NOLOCK\s*(?=,|\))", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockListHintPattern();
}
