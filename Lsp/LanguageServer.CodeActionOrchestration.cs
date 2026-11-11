using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Coordinates the code actions returned for an LSP request.</summary>
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

        // Fixes for the findings come first, then generators, then context and maintenance actions.
        AddDiagnosticActions(actions, uri, document?.Text, diagnostics);
        AddExperimentalActions(actions, uri, document?.Text, diagnostics, parameters);
        AddRepositoryContextAction(actions, parameters, document?.Text);
        AddDependencyQueryActions(actions, parameters, document?.Text);

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

    /// <summary>
    /// Adds the fixes of every finding first, then the suppressions grouped by scope: line, file, repository.
    /// </summary>
    private void AddDiagnosticActions(
        List<object> actions,
        string uri,
        string? text,
        IReadOnlyList<LintDiagnostic> diagnostics)
    {
        var reindexActionAdded = false;
        List<object> lineSuppressions = [];
        List<object> fileSuppressions = [];
        List<object> repositorySuppressions = [];

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

            AddReplacementActions(actions, uri, text, diagnostic);
            CollectSuppressionActions(lineSuppressions, fileSuppressions, repositorySuppressions, uri, text, diagnostic);
        }

        actions.AddRange(lineSuppressions);
        actions.AddRange(fileSuppressions);
        actions.AddRange(repositorySuppressions);
    }

    private static bool IsStaleDiagnostic(LintDiagnostic diagnostic)
    {
        return diagnostic.IsStale || diagnostic.Source == "stale-index";
    }

    private void CollectSuppressionActions(
        List<object> lineActions,
        List<object> fileActions,
        List<object> repositoryActions,
        string uri,
        string? text,
        LintDiagnostic diagnostic)
    {
        if (SuppressLintAction(uri, text, diagnostic) is { } lineAction)
        {
            lineActions.Add(lineAction);
        }

        if (SuppressFileAction(uri, text, diagnostic) is { } fileAction)
        {
            fileActions.Add(fileAction);
        }

        if (SuppressRepositoryAction(diagnostic) is { } repositoryAction)
        {
            repositoryActions.Add(repositoryAction);
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
}
