using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
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
        bool includeRefactorCandidate = true,
        string? suppressionScope = null
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

        Dictionary<string, object?> data = new()
        {
            ["source"] = metadata.Source,
            ["confidence"] = metadata.Confidence,
            ["diagnosticConfidence"] = diagnosticConfidence,
            ["stale"] = metadata.IsStale,
            ["generation"] = "parse-validated-rewrite",
            ["writesFile"] = false,
            ["isRefactorCandidate"] = includeRefactorCandidate,
            ["experimental"] = experimental,
            ["rule"] = diagnosticRule,
            ["suppressionScope"] = suppressionScope,
            ["applicability"] = applicability,
            ["explanation"] = ExplanationMetadataFor(diagnosticRule),
            ["references"] = actionReferences,
            ["diff"] = edits.Select(edit => new { range = edit.Range, newText = edit.NewText }).ToArray(),
        };

        if (refactorCandidate is not null)
        {
            data["refactorCandidate"] = refactorCandidate;
        }

        return data;
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

        if (queries is null)
        {
            return null;
        }

        if (!queries.IsIndexCurrent())
        {
            return null;
        }

        var enriched = RefactorCandidatePreviewService.AddDiagnosticContext(
            candidate,
            document,
            queries);

        if (!RefactorCandidatePreviewService.IsSafeCandidate(enriched))
        {
            return null;
        }

        var sourceFile = LspUri.PathFromUri(uri);

        if (!RefactorCandidatePreviewService.IsParseValidPreview(
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
}
