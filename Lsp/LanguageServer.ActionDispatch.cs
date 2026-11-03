namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private void AddReplacementActions(List<object> actions, string uri, string? text, LintDiagnostic diagnostic)
    {
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
        if (NullComparisonAction(uri, text, diagnostic) is { } nullAction)
        {
            actions.Add(nullAction);
        }

        if (AliasReplacementAction(uri, text, diagnostic) is { } aliasAction)
        {
            actions.Add(aliasAction);
        }

        if (SimilarColumnAction(uri, text, diagnostic) is { } suggestionAction)
        {
            actions.Add(suggestionAction);
        }

        if (DerivedQueryColumnAction(uri, text, diagnostic) is { } derivedColumnAction)
        {
            actions.Add(derivedColumnAction);
        }

        actions.AddRange(AmbiguousColumnActions(uri, text, diagnostic));
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
