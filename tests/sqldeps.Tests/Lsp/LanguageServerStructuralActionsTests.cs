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
                && action.Contains("known column metadata", StringComparison.Ordinal)
                && action.Contains("\"experimental\":true", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSelectStarExpansionWithTableVariableColumns()
    {
        const string text = "DECLARE @Rows TABLE (id INT, title VARCHAR(10));\nSELECT * FROM @Rows;";
        var actions = Preview(text, []);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && action.Contains("id, title", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSelectStarExpansionWithTemporaryTableColumns()
    {
        const string text = "CREATE TABLE #Rows (id INT, title VARCHAR(10));\nSELECT * FROM #Rows;";
        var actions = Preview(text, []);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && action.Contains("id, title", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSelectStarExpansionWithDerivedTableColumns()
    {
        const string text = "SELECT * FROM (SELECT 1 AS id, 'x' AS title) AS source;";
        var actions = Preview(text, []);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && action.Contains("id, title", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSelectStarExpansionWithCteColumns()
    {
        const string text = "WITH active (id, title) AS (SELECT 1, 'x') SELECT * FROM active;";
        var actions = Preview(text, []);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && action.Contains("id, title", StringComparison.Ordinal));
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
    public void PreviewCodeActions_OffersPrimaryKeyFilterWhenParameterEvidenceIsKnown()
    {
        const string text = "DECLARE @id INT;\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("id", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Add primary-key filter", StringComparison.Ordinal)
                && action.Contains(" WHERE id = @id", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_UsesExplicitPrimaryKeyParameterMapping()
    {
        const string text = "DECLARE @productId INT;\n-- sqldeps: key-parameter=@productId -> prCode\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("prCode", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "prCode", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE prCode = @productId", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_KeepsSafeTemplateWhenPrimaryKeyParameterIsMissing()
    {
        const string text = "UPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("id", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.DoesNotContain(
            serializedActions,
            action => action.Contains("Add primary-key filter", StringComparison.Ordinal));
        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE 1 = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_UsesStoredProcedureParameterAsPrimaryKeyEvidence()
    {
        const string text = "CREATE PROCEDURE dbo.UpdateInfo @id INT AS\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("id", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE id = @id", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_UsesAllCompositePrimaryKeyParameters()
    {
        const string text = "DECLARE @tenantId INT, @id INT;\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("tenantId", "id", "title"),
            [
                new TableKey("dbo.Info", "primary-key", "PK_Info", "tenantId", 0, "index.sql", 1),
                new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 1, "index.sql", 1),
            ]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE tenantId = @tenantId AND id = @id", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_DoesNotReuseProcedureParametersAcrossScopes()
    {
        const string text = "CREATE PROCEDURE dbo.WithId @id INT AS\nUPDATE dbo.Info SET title = 'x';\nGO\nCREATE PROCEDURE dbo.WithoutId @other INT AS\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("id", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Single(
            serializedActions,
            action => action.Contains(" WHERE id = @id", StringComparison.Ordinal));
        Assert.DoesNotContain(
            serializedActions,
            action => action.Contains(" WHERE id = @other", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersPrimaryKeyFilterForParameterizedDelete()
    {
        const string text = "DECLARE @id INT;\nDELETE FROM dbo.Info;";
        var actions = Preview(
            text,
            TableColumns("id", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "id", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE id = @id", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_OffersSafeWhereTemplateForUnfilteredDelete()
    {
        var actions = Preview("DELETE FROM dbo.Info;", TableColumns("id", "title"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Insert safe WHERE 1 = 0 template", StringComparison.Ordinal)
                && action.Contains(" WHERE 1 = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewCodeActions_KeepsSafeTemplateForIncompletePrimaryKeyMapping()
    {
        const string text = "DECLARE @productId INT;\n-- sqldeps: key-parameter=@productId -> otherColumn\nUPDATE dbo.Info SET title = 'x';";
        var actions = Preview(
            text,
            TableColumns("prCode", "title"),
            [new TableKey("dbo.Info", "primary-key", "PK_Info", "prCode", 0, "index.sql", 1)]);
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.DoesNotContain(
            serializedActions,
            action => action.Contains(" WHERE prCode = @productId", StringComparison.Ordinal));
        Assert.Contains(
            serializedActions,
            action => action.Contains(" WHERE 1 = 0", StringComparison.Ordinal));
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
    public void PreviewCodeActions_LeavesRefactorCandidateOutWhenImpactIsUnavailable()
    {
        var actions = Preview("SELECT * FROM dbo.Info;", TableColumns("id", "title"));
        var serializedActions = actions.Select(action => JsonSerializer.Serialize(action)).ToArray();

        Assert.Contains(
            serializedActions,
            action => action.Contains("Expand SELECT * with indexed columns", StringComparison.Ordinal)
                && !action.Contains("\"refactorCandidate\"", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("SELECT * FROM dbo.Info;", "select-star")]
    [InlineData("UPDATE dbo.Info SET title = 'x';", "update-without-where")]
    [InlineData("DELETE FROM dbo.Info;", "delete-without-where")]
    [InlineData("SELECT id FROM dbo.Info ORDER BY id OFFSET -1 ROWS;", "negative-offset")]
    [InlineData("SELECT id FROM dbo.Info ORDER BY id OFFSET 0 ROWS FETCH NEXT 0 ROWS ONLY;", "invalid-fetch")]
    [InlineData("DECLARE @id INT = 1; EXEC(N'SELECT * FROM dbo.Info WHERE id = ' + @id);", "dynamic-sql-concatenation")]
    public void PreviewCodeActions_ProjectsCurrentAdvisorCandidatesWithSafeMetadata(
        string text,
        string rule)
    {
        object[] actions = Preview(text, TableColumns("id", "title"));
        JsonElement[] actionElements = actions
            .Select(action => JsonSerializer.SerializeToElement(action))
            .ToArray();

        RefactorCandidate candidate = Assert.Single(
            RefactorCandidateProjector.Project(actionElements),
            item => item.Rule == rule);

        Assert.False(candidate.WritesFile);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Applicability));
        Assert.NotEmpty(candidate.Preview.Changes);
    }

    private static object[] Preview(
        string text,
        IReadOnlyList<TableColumn> tableColumns,
        IReadOnlyList<TableKey>? tableKeys = null)
    {
        string sourceFile = Path.Combine(Path.GetTempPath(), "sqldeps-structural-actions.sql");
        IndexAnalysisMetadata indexed = new(
            new MetadataCatalog([], tableColumns, tableKeys ?? [], [], []),
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
