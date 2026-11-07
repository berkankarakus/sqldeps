using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class QueryColumnValidatorTests
{
    [Fact]
    public void Validate_ReportsUnknownQualifiedColumnWithSuggestion()
    {
        var findings = Validate(
            "SELECT u.CustmerId FROM dbo.Users AS u WHERE u.CustomerId = 1;",
            [new TableColumn("dbo.Users", "CustomerId", "int", "schema.sql", 1)]
        );

        var finding = Assert.Single(findings, item => item.Rule == "unknown-column");
        Assert.Contains("CustmerId", finding.Message);
        Assert.Contains("Did you mean 'CustomerId'?", finding.Message);
        Assert.Equal(1, finding.Line);
        Assert.Equal(8, finding.Column);
        Assert.Equal(11, finding.Length);
    }

    [Fact]
    public void Validate_DoesNotReportKnownQualifiedColumn()
    {
        var findings = Validate(
            "SELECT u.CustomerId FROM dbo.Users AS u WHERE u.CustomerId = 1;",
            [new TableColumn("dbo.Users", "CustomerId", "int", "schema.sql", 1)]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "unknown-column");
    }

    [Fact]
    public void Validate_ReportsUnknownTableWhenMetadataIsMissing()
    {
        var findings = Validate(
            "SELECT id FROM dbo.Missing;",
            [new TableColumn("dbo.Known", "Id", "int", "schema.sql", 1)]
        );

        var finding = Assert.Single(findings, item => item.Rule == "unknown-table");
        Assert.Contains("dbo.Missing", finding.Message);
        Assert.Equal("warning", finding.Severity);
        Assert.True(finding.Line > 0);
        Assert.True(finding.Column > 0);
        Assert.True(finding.Length > 0);
    }

    [Fact]
    public void Validate_DoesNotGuessUnknownTablesWithoutAnyCatalogMetadata()
    {
        var findings = Validate(
            "SELECT id FROM dbo.Missing;",
            []
        );

        Assert.DoesNotContain(findings, item => item.Rule == "unknown-table");
    }

    [Fact]
    public void Validate_PrefersLocalSubqueryColumnsOverOuterScope()
    {
        var findings = Validate(
            "CREATE TABLE dbo.ScopeOuter (id INT); CREATE TABLE dbo.ScopeInner (id INT); SELECT (SELECT id FROM dbo.ScopeInner) AS inner_id FROM dbo.ScopeOuter;",
            [
                new TableColumn("dbo.ScopeOuter", "id", "int", "schema.sql", 1),
                new TableColumn("dbo.ScopeInner", "id", "int", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "ambiguous-column");
    }

    [Fact]
    public void Validate_ReportsUnknownColumnInsideSubqueryWithRange()
    {
        IReadOnlyList<LintFinding> findings = Validate(
            "SELECT (SELECT src.Missing FROM dbo.ScopeInner AS src) FROM dbo.ScopeOuter;",
            [new TableColumn("dbo.ScopeInner", "Id", "int", "schema.sql", 1)]
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unknown-column");

        Assert.Equal("error", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.Equal("src.Missing".Length, finding.Length);
    }

    [Fact]
    public void Validate_ReportsAmbiguousColumnWithDiagnosticRange()
    {
        var findings = Validate(
            "SELECT id FROM dbo.Users u JOIN dbo.Orders o ON u.id = o.id;",
            [
                new TableColumn("dbo.Users", "id", "int", "schema.sql", 1),
                new TableColumn("dbo.Orders", "id", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "ambiguous-column");
        Assert.Equal("error", finding.Severity);
        Assert.Contains("Qualify it with a table alias", finding.Message);
        Assert.True(finding.Line > 0);
        Assert.True(finding.Column > 0);
        Assert.True(finding.Length > 0);
    }

    [Fact]
    public void Validate_ReportsAmbiguousTableWithDiagnosticRange()
    {
        var findings = Validate(
            "SELECT id FROM Users;",
            [
                new TableColumn("dbo.Users", "id", "int", "schema.sql", 1),
                new TableColumn("sales.Users", "id", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "ambiguous-table");
        Assert.Equal("error", finding.Severity);
        Assert.Contains("qualify it as 'dbo.Users'", finding.Message);
        Assert.True(finding.Line > 0);
        Assert.True(finding.Column > 0);
        Assert.True(finding.Length > 0);
    }

    [Fact]
    public void Validate_ResolvesDerivedTableAliasColumns()
    {
        var findings = Validate(
            "SELECT q.id FROM (SELECT u.id FROM dbo.Users u) q;",
            [new TableColumn("dbo.Users", "id", "int", "schema.sql", 1)]
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unbound-identifier" or "unknown-column");
    }

    [Fact]
    public void Validate_ReportsMissingRequiredInsertColumn()
    {
        var findings = Validate(
            "INSERT INTO dbo.Users (DisplayName) VALUES ('Ada');",
            [
                new TableColumn(
                    "dbo.Users",
                    "Id",
                    "int",
                    "schema.sql",
                    1,
                    IsNullable: false),
                new TableColumn(
                    "dbo.Users",
                    "DisplayName",
                    "varchar",
                    "schema.sql",
                    1,
                    IsNullable: true),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "missing-required-insert-column");
        Assert.Contains("Id", finding.Message);
    }

    [Fact]
    public void Validate_ReportsInsertValueCountWhenTargetColumnsAreImplicit()
    {
        var findings = Validate(
            "INSERT INTO dbo.Users VALUES (1);",
            [
                new TableColumn("dbo.Users", "Id", "int", "schema.sql", 1),
                new TableColumn("dbo.Users", "DisplayName", "varchar", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "insert-value-count");
        Assert.Contains("2 table columns", finding.Message);
        Assert.Contains("1 values", finding.Message);
    }

    [Fact]
    public void Validate_ReportsDuplicateAndUnknownInsertColumns()
    {
        var findings = Validate(
            "INSERT INTO dbo.Users (Id, Id, Missing) VALUES (1, 2, 3);",
            [new TableColumn("dbo.Users", "Id", "int", "schema.sql", 1)]
        );

        Assert.Contains(findings, item => item.Rule == "duplicate-insert-column");
        Assert.Contains(findings, item => item.Rule == "unknown-insert-column");
    }

    [Fact]
    public void Validate_ResolvesTableVariableColumns()
    {
        var findings = Validate(
            "DECLARE @Rows TABLE (Id INT); SELECT rowsAlias.Id FROM @Rows AS rowsAlias;",
            []
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unknown-table" or "unknown-column" or "unbound-identifier");
    }

    [Fact]
    public void Validate_ReportsUnknownColumnForTableVariableWithRange()
    {
        IReadOnlyList<LintFinding> findings = Validate(
            "DECLARE @Rows TABLE (Id INT); SELECT rowsAlias.Missing FROM @Rows AS rowsAlias;",
            []
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unknown-column");

        Assert.Equal("error", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.Equal("rowsAlias.Missing".Length, finding.Length);
    }

    [Fact]
    public void Validate_ResolvesTemporaryTableColumns()
    {
        var findings = Validate(
            "CREATE TABLE #Rows (Id INT); SELECT rowsAlias.Id FROM #Rows AS rowsAlias;",
            []
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unknown-table" or "unknown-column" or "unbound-identifier");
    }

    [Fact]
    public void Validate_ReportsUnknownColumnForTemporaryTableWithRange()
    {
        IReadOnlyList<LintFinding> findings = Validate(
            "CREATE TABLE #Rows (Id INT); SELECT rowsAlias.Missing FROM #Rows AS rowsAlias;",
            []
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unknown-column");

        Assert.Equal("error", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.Equal("rowsAlias.Missing".Length, finding.Length);
    }

    [Fact]
    public void Validate_ResolvesCorrelatedOuterAlias()
    {
        var findings = Validate(
            "SELECT o.Id FROM dbo.Orders AS o WHERE EXISTS (SELECT 1 FROM dbo.OrderItems AS i WHERE i.OrderId = o.Id);",
            [
                new TableColumn("dbo.Orders", "Id", "int", "schema.sql", 1),
                new TableColumn("dbo.OrderItems", "OrderId", "int", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unknown-column" or "unbound-identifier");
    }

    [Fact]
    public void Validate_ReportsUnknownColumnForCorrelatedOuterAliasWithRange()
    {
        IReadOnlyList<LintFinding> findings = Validate(
            "SELECT o.Missing FROM dbo.Orders AS o WHERE EXISTS (SELECT 1 FROM dbo.OrderItems AS i WHERE i.OrderId = o.Id);",
            [
                new TableColumn("dbo.Orders", "Id", "int", "schema.sql", 1),
                new TableColumn("dbo.OrderItems", "OrderId", "int", "schema.sql", 1),
            ]
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unknown-column");

        Assert.Equal("error", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.Equal("o.Missing".Length, finding.Length);
    }

    private static IReadOnlyList<LintFinding> Validate(
        string sql,
        IReadOnlyList<TableColumn> tableColumns
    )
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        Assert.Empty(errors);

        var script = Assert.IsType<TSqlScript>(fragment);
        var batches = new[] { new ParsedSqlBatch(script, sql, 1) };
        var metadata = new MetadataCatalog(
            [],
            tableColumns,
            [],
            [],
            []
        );

        return QueryColumnValidator.Validate(
            "query.sql",
            batches,
            metadata,
            LocalColumnCollector.Collect(script, 1)
        ).ToArray();
    }
}
