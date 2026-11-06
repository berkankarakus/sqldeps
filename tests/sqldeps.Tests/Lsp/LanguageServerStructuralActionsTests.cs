using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class LanguageServerStructuralActionsTests
{
    [Fact]
    public void PreviewCodeActions_OffersSelectStarExpansionWithIndexedColumns()
    {
        const string text = "SELECT * FROM dbo.Info;";
        var actions = Preview(text, TableColumns("id", "title"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && action.Contains("id, title", StringComparison.Ordinal)
                && action.Contains("\"experimental\":true", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSafeWhereTemplateForUnfilteredUpdate()
    {
        var actions = Preview("UPDATE dbo.Info SET title = 'x';", TableColumns("id", "title"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Insert safe WHERE 1 = 0 template", StringComparison.Ordinal)
                && action.Contains(" WHERE 1 = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersPagingFixesForInvalidOffsetAndFetch()
    {
        const string text = "SELECT id FROM dbo.Info ORDER BY id OFFSET -1 ROWS FETCH NEXT 0 ROWS ONLY;";
        var actions = Preview(text, TableColumns("id"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Set OFFSET to 0", StringComparison.Ordinal)
                && action.Contains("\"newText\":\"0\"", StringComparison.Ordinal));
        Assert.Contains(
            serializedActions,
            action => action.Contains("Set FETCH count to 1", StringComparison.Ordinal)
                && action.Contains("\"newText\":\"1\"", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_DoesNotOfferLowConfidenceImplicitCast()
    {
        const string text = "SELECT id FROM dbo.Info WHERE id = '1';";
        var actions = Preview(text, TableColumns("id"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("implicit-filter-conversion", StringComparison.Ordinal));
        Assert.DoesNotContain(
            serializedActions,
            action => action.Contains("CAST('1' AS INT)", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_EmbedsTypedRefactorCandidateForEditableAction()
    {
        var actions = Preview("SELECT * FROM dbo.Info;", TableColumns("id", "title"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("\"refactorCandidate\"", StringComparison.Ordinal)
                && action.Contains("\"rule\":\"select-star\"", StringComparison.Ordinal)
                && action.Contains("\"status\":\"unavailable\"", StringComparison.Ordinal)
                && action.Contains("\"writes_file\":false", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSafeDynamicSqlParameterization()
    {
        const string text = "DECLARE @id INT = 1;\nEXEC(N'SELECT * FROM dbo.Info WHERE id = ' + @id);";
        object[] actions = Preview(text, []);
        string[] serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Convert dynamic SQL to sp_executesql parameters", StringComparison.Ordinal)
                && action.Contains("@__sqldeps_0", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_DoesNotParameterizeDynamicObjectNames()
    {
        const string text = "DECLARE @tableName NVARCHAR(128);\nEXEC(N'SELECT * FROM ' + @tableName);";
        object[] actions = Preview(text, []);
        string[] serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.DoesNotContain(
            serializedActions,
            action => action.Contains("Convert dynamic SQL to sp_executesql parameters", StringComparison.Ordinal));
    }

    private static object[] Preview(
        string text,
        IReadOnlyList<TableColumn> tableColumns)
    {
        string sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-structural-actions.sql");
        IndexAnalysisMetadata indexed = new(
            new MetadataCatalog([], tableColumns, [], [], []),
            [new DatabaseObject("dbo.Info", "table", "index.sql", 1)],
            []);

        return LanguageServer.PreviewCodeActions(
            indexed,
            sourceFile,
            text,
            ObjectNames.StandardDefaultSchema);
    }

    private static IReadOnlyList<TableColumn> TableColumns(params string[] names)
    {
        return names
            .Select(name => new TableColumn(
                "dbo.Info",
                name,
                string.Equals(name, "id", StringComparison.OrdinalIgnoreCase) ? "int" : "varchar",
                "index.sql",
                1,
                IsNullable: true,
                Precision: null,
                Scale: null))
            .ToArray();
    }
}
