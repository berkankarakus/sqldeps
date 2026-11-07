namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
        var replacementActionCount = actions.Count;

        if (experimentalSuggestions && IndexSuggestionAction(diagnostic) is { } indexSuggestionAction)
        {
            actions.Add(indexSuggestionAction);
        }

        if (ResultSetExplanationAction(diagnostic) is { } resultSetAction)
        {
            actions.Add(resultSetAction);
            return;
        }

        if (ReviewExplanationAction(diagnostic) is { } reviewAction)
        {
            actions.Add(reviewAction);
        }

        if (ShouldSkipLowConfidenceActions(diagnostic))
        {
            return;
        }

        if (IsOutsideDocumentRange(text, diagnostic.Range))
        {
            return;
        }

        AddBasicReplacementActions(actions, uri, text, diagnostic);
        if (experimentalSuggestions)
        {
            AddStructuralReplacementActions(actions, uri, text, diagnostic);
        }

        if (FallbackExplanationAction(actions, replacementActionCount, diagnostic) is { } explanationAction)
        {
            actions.Add(explanationAction);
        }
    }

    private object? ReviewExplanationAction(LintDiagnostic diagnostic)
    {
        if (!IsReviewOnlyRule(diagnostic.Rule))
        {
            return null;
        }

        return DiagnosticExplanationAction(diagnostic);
    }

    private object? FallbackExplanationAction(
        List<object> actions,
        int replacementActionCount,
        LintDiagnostic diagnostic
    )
    {
        if (IsReviewOnlyRule(diagnostic.Rule)
            || actions.Count != replacementActionCount)
        {
            return null;
        }

        return DiagnosticExplanationAction(diagnostic);
    }

    private static bool ShouldSkipLowConfidenceActions(LintDiagnostic diagnostic)
    {
        return diagnostic.Confidence == "low" && diagnostic.Rule != "nolock";
    }

    private static bool IsOutsideDocumentRange(string? text, LspRange range)
    {
        return text is not null && !IsDocumentRange(text, range);
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

        if (experimentalSuggestions && DynamicSqlParameterAction(uri, text, diagnostic) is { } dynamicSqlAction)
        {
            actions.Add(dynamicSqlAction);
        }

        if (experimentalSuggestions && DerivedQueryColumnAction(uri, text, diagnostic) is { } derivedColumnAction)
        {
            actions.Add(derivedColumnAction);
        }

        actions.AddRange(AmbiguousColumnActions(uri, text, diagnostic));
    }

    private object? NoLockAction(string uri, string? text, LintDiagnostic diagnostic)
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
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            diagnosticRule: diagnostic.Rule,
            applicability: "The table hint must be removable without changing the intended isolation semantics.");
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

        if (candidate.Match is null)
        {
            return false;
        }

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
