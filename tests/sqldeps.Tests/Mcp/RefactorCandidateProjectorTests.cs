using System.Text.Json;
using SqlDeps.Core;

namespace SqlDeps.Tests.Mcp;

public sealed class RefactorCandidateProjectorTests
{
    [Fact]
    public void Project_ReturnsOnlySafeEvidenceBackedEditableActions()
    {
        var actions = ParseActions("""
            [
              {
                "title": "Expand SELECT *",
                "kind": "quickfix",
                "data": {
                  "writesFile": false,
                  "diagnosticConfidence": "high",
                  "confidence": "medium",
                  "rule": "select-star",
                  "applicability": "Indexed columns are complete.",
                  "references": [{"title":"Rule", "note":"Evidence", "url":"https://example.test/rule"}]
                },
                "edit": {"changes": {"file:///query.sql": [{"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"newText":"SELECT id"}]}}
              },
              {
                "title": "Review diagnostic",
                "data": {"writesFile": false, "confidence": "medium"},
                "command": {"command":"sqldeps.explain-diagnostic"}
              },
              {
                "title": "Unsafe edit",
                "data": {"writesFile": true, "confidence": "high"},
                "edit": {"changes": {"file:///query.sql": []}}
              },
              {
                "title": "Stale edit",
                "data": {"writesFile": false, "confidence": "high", "stale": true},
                "edit": {"changes": {"file:///query.sql": []}}
              }
            ]
            """);

        var impact = new RefactorDependencyImpact(
            "complete",
            null,
            [new RefactorDependencyItem("dbo.Info", [])]);
        var candidates = RefactorCandidateProjector.Project(actions, impact);
        var candidate = Assert.Single(candidates);
        var document = JsonSerializer.SerializeToElement(candidate);

        Assert.Equal("Expand SELECT *", document.GetProperty("title").GetString());
        Assert.Equal("select-star", document.GetProperty("rule").GetString());
        Assert.Equal("high", document.GetProperty("confidence").GetString());
        Assert.Equal("Indexed columns are complete.", document.GetProperty("applicability").GetString());
        Assert.False(document.GetProperty("writes_file").GetBoolean());
        Assert.True(document.GetProperty("preview").TryGetProperty("changes", out _));
        Assert.Single(document.GetProperty("references").EnumerateArray());
        Assert.Equal("complete", document.GetProperty("affected_dependencies").GetProperty("status").GetString());
        var fileEdits = candidate.Preview.Changes["file:///query.sql"];

        Assert.Single(fileEdits);
        Assert.Equal("SELECT id", fileEdits[0].NewText);
        Assert.Equal(0, fileEdits[0].Range.Start.Line);
    }

    [Fact]
    public void Project_RejectsEditableActionsWithoutConfidenceOrSafeWriteFlag()
    {
        var actions = ParseActions("""
            [
              {
                "title": "No confidence",
                "data": {"writesFile": false},
                "edit": {"changes": {}}
              },
              {
                "title": "No safe flag",
                "data": {"confidence": "high"},
                "edit": {"changes": {}}
              },
              {
                "title": "Invalid range",
                "data": {"writesFile": false, "confidence": "high", "rule": "select-star"},
                "edit": {"changes": {"file:///query.sql": [{"range":{"start":{"line":-1,"character":0},"end":{"line":0,"character":1}},"newText":"id"}]}}
              }
            ]
            """);

        var candidates = RefactorCandidateProjector.Project(actions);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Project_RejectsActionsWithUnknownConfidence()
    {
        var actions = ParseActions("""
            [
              {
                "title": "Unknown confidence",
                "data": {"writesFile": false, "confidence": "speculative", "rule": "select-star"},
                "edit": {"changes": {"file:///query.sql": [{"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"newText":"id"}]}}
              }
            ]
            """);

        Assert.Empty(RefactorCandidateProjector.Project(actions));
    }

    [Fact]
    public void Project_RejectsStaleAndFileWritingActionsWithValidEdits()
    {
        var actions = ParseActions("""
            [
              {
                "title": "Stale edit",
                "data": {"writesFile": false, "confidence": "high", "stale": true},
                "edit": {"changes": {"file:///query.sql": [{"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"newText":"id"}]}}
              },
              {
                "title": "File writing edit",
                "data": {"writesFile": true, "confidence": "high"},
                "edit": {"changes": {"file:///query.sql": [{"range":{"start":{"line":0,"character":7},"end":{"line":0,"character":8}},"newText":"id"}]}}
              }
            ]
            """);

        var candidates = RefactorCandidateProjector.Project(actions);

        Assert.Empty(candidates);
    }

    private static JsonElement[] ParseActions(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement
            .EnumerateArray()
            .Select(action => action.Clone())
            .ToArray();
    }

}
