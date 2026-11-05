namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        if (IndexSuggestionAction(diagnostic) is { } indexSuggestionAction)
        {
            actions.Add(indexSuggestionAction);
        }

        if (DiagnosticReviewAction(diagnostic) is { } reviewAction)
        {
            actions.Add(reviewAction);
        }

        if (ResultSetExplanationAction(diagnostic) is { } resultSetAction)
        {
            actions.Add(resultSetAction);
            return;
        }

        if (diagnostic.Confidence == "low")
        {
            return;
        }

        if (text is not null && !IsDocumentRange(text, diagnostic.Range))
        {
            return;
        }

        AddBasicReplacementActions(actions, uri, text, diagnostic);
        AddStructuralReplacementActions(actions, uri, text, diagnostic);
    }

    private void AddBasicReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        if (NoLockAction(uri, text, diagnostic) is { } noLockAction)
        {
            actions.Add(noLockAction);
        }

        if (NullComparisonAction(uri, text, diagnostic) is { } nullAction)
        {
            actions.Add(nullAction);
        }

        if (NotInNullAction(uri, text, diagnostic) is { } notInNullAction)
        {
            actions.Add(notInNullAction);
        }

        if (AliasReplacementAction(uri, text, diagnostic) is { } aliasAction)
        {
            actions.Add(aliasAction);
        }

        if (AmbiguousTableAction(uri, text, diagnostic) is { } ambiguousTableAction)
        {
            actions.Add(ambiguousTableAction);
        }

        if (SimilarColumnAction(uri, text, diagnostic) is { } suggestionAction)
        {
            actions.Add(suggestionAction);
        }

        if (DynamicSqlParameterAction(uri, text, diagnostic) is { } dynamicSqlAction)
        {
            actions.Add(dynamicSqlAction);
        }

        if (DerivedQueryColumnAction(uri, text, diagnostic) is { } derivedColumnAction)
        {
            actions.Add(derivedColumnAction);
        }

        actions.AddRange(AmbiguousColumnActions(uri, text, diagnostic));
    }

    private static object? NoLockAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (diagnostic.Rule != "nolock" || text is null)
        {
            return null;
        }

        if (!TryGetNoLockEdit(text, diagnostic.Range, out var range, out var replacement))
        {
            return null;
        }

        return QuickFix(
            "Remove NOLOCK table hint",
            uri,
            range,
            replacement,
            text,
            diagnostic.Confidence
        );
    }

    private static bool TryGetNoLockEdit(
        string text,
        LspRange diagnosticRange,
        out LspRange range,
        out string replacement
    )
    {
        range = default!;
        replacement = "";

        if (!IsDocumentRange(text, diagnosticRange))
        {
            return false;
        }

        var searchStart = OffsetAt(text, diagnosticRange.Start.Line, diagnosticRange.Start.Character);
        var statementEnd = text.IndexOf(';', searchStart);
        if (statementEnd < 0)
        {
            statementEnd = text.Length;
        }

        if (statementEnd <= searchStart)
        {
            return false;
        }

        var statementTail = text[searchStart..statementEnd];
        var candidates = new[]
        {
            (Match: NoLockOnlyHintPattern().Match(statementTail), Replacement: ""),
            (Match: NoLockFirstHintPattern().Match(statementTail), Replacement: "WITH ("),
            (Match: NoLockListHintPattern().Match(statementTail), Replacement: ""),
        };
        var candidate = candidates
            .Where(item => item.Match.Success)
            .OrderBy(item => item.Match.Index)
            .FirstOrDefault();

        if (!candidate.Match.Success)
        {
            return false;
        }

        var start = searchStart + candidate.Match.Index;
        var end = start + candidate.Match.Length;
        range = new(PositionAt(text, start).Start, PositionAt(text, end).Start);
        replacement = candidate.Replacement;

        return true;
    }

    private void AddStructuralReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        if (SelectStarAction(uri, text, diagnostic) is { } selectStarAction)
        {
            actions.Add(selectStarAction);
        }
        else if (MultiTableSelectStarAction(uri, text, diagnostic) is { } multiTableAction)
        {
            actions.Add(multiTableAction);
        }

        if (WhereTemplateAction(uri, text, diagnostic) is { } whereAction)
        {
            actions.Add(whereAction);
        }

        if (ImplicitFilterConversionAction(uri, text, diagnostic) is { } conversionAction)
        {
            actions.Add(conversionAction);
        }

        if (PagingLiteralAction(uri, text, diagnostic) is { } pagingAction)
        {
            actions.Add(pagingAction);
        }

        if (MissingProcedureParameterAction(uri, text, diagnostic) is { } parameterAction)
        {
            actions.Add(parameterAction);
        }

        if (MissingRequiredInsertColumnAction(uri, text, diagnostic) is { } insertColumnAction)
        {
            actions.Add(insertColumnAction);
        }
    }
}
