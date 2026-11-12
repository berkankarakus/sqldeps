using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
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
    public void Validate_DoesNotBlameDerivedTableWhenAnotherSourceIsUnresolved()
    {
        var findings = Validate(
            "SELECT col_c FROM dbo.MissingSource AS u JOIN (SELECT @col_a AS col_a) AS t ON t.col_a = u.col_a;",
            [new TableColumn("dbo.KnownSource", "Id", "int", "schema.sql", 1)]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "unknown-column");
    }

    [Fact]
    public void Validate_DoesNotReportPhysicalIndexWarningForDerivedTableColumn()
    {
        var findings = Validate(
            "SELECT u.col_a FROM dbo.KnownSource AS u JOIN (SELECT @col_a AS col_a) AS t ON t.col_a = u.col_a;",
            [new TableColumn("dbo.KnownSource", "col_a", "int", "schema.sql", 1)]
        );

        Assert.DoesNotContain(
            findings,
            item => item.Rule == "unindexed-join" && item.Message.Contains("'t.col_a'"));
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
    public void Validate_ReportsUnindexedWhereWithInfoSeverityAndRange()
    {
        IReadOnlyList<LintFinding> findings = Validate(
            "SELECT u.Id FROM dbo.Users AS u WHERE u.Name = 'Ada';",
            [
                new TableColumn("dbo.Users", "Id", "int", "schema.sql", 1),
                new TableColumn("dbo.Users", "Name", "nvarchar", "schema.sql", 1),
            ]
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unindexed-where");

        Assert.Equal("info", finding.Severity);
        Assert.Equal(1, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.Equal("u.Name".Length, finding.Length);
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
    public void Validate_ResolvesDerivedTableStarColumnsFromIndexedSource()
    {
        var findings = Validate(
            "SELECT q.id FROM (SELECT u.* FROM dbo.Users u) q;",
            [
                new TableColumn("dbo.Users", "id", "int", "schema.sql", 1),
                new TableColumn("dbo.Users", "display_name", "nvarchar", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unbound-identifier" or "unknown-column");
    }

    [Fact]
    public void Validate_ResolvesCteStarColumnsFromIndexedSource()
    {
        var findings = Validate(
            "WITH users_cte AS (SELECT u.* FROM dbo.Users u) SELECT users_cte.id FROM users_cte;",
            [
                new TableColumn("dbo.Users", "id", "int", "schema.sql", 1),
                new TableColumn("dbo.Users", "display_name", "nvarchar", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule is "unbound-identifier" or "unknown-column");
    }

    [Fact]
    public void Validate_ReportsUnknownColumnAgainstResolvedCteStarColumns()
    {
        var findings = Validate(
            "WITH users_cte AS (SELECT u.* FROM dbo.Users u) SELECT users_cte.missing FROM users_cte;",
            [new TableColumn("dbo.Users", "id", "int", "schema.sql", 1)]
        );

        LintFinding finding = Assert.Single(findings, item => item.Rule == "unknown-column");

        Assert.Contains("missing", finding.Message);
        Assert.Contains("users_cte", finding.Message);
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

    [Fact]
    public void Validate_ReportsNotInNullableSubqueryColumn()
    {
        var findings = Validate(
            "SELECT o.Id FROM dbo.Orders AS o WHERE o.Id NOT IN (SELECT i.OrderId FROM dbo.OrderItems AS i);",
            [
                new TableColumn("dbo.Orders", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.OrderItems", "OrderId", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "not-in-null");
        Assert.Equal("warning", finding.Severity);
        Assert.Contains("dbo.OrderItems.OrderId", finding.Message);
        Assert.Contains("NOT EXISTS", finding.Message);
    }

    [Fact]
    public void Validate_DoesNotReportNotInForNonNullableOrUnknownSubqueryColumns()
    {
        var nonNullableFindings = Validate(
            "SELECT o.Id FROM dbo.Orders AS o WHERE o.Id NOT IN (SELECT i.OrderId FROM dbo.OrderItems AS i);",
            [
                new TableColumn("dbo.Orders", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.OrderItems", "OrderId", "int", "schema.sql", 1, IsNullable: false),
            ]
        );
        var unknownFindings = Validate(
            "SELECT o.Id FROM dbo.Orders AS o WHERE o.Id NOT IN (SELECT i.OrderId FROM dbo.OrderItems AS i);",
            [new TableColumn("dbo.Orders", "Id", "int", "schema.sql", 1, IsNullable: false)]
        );

        Assert.DoesNotContain(nonNullableFindings, item => item.Rule == "not-in-null");
        Assert.DoesNotContain(unknownFindings, item => item.Rule == "not-in-null");
    }

    [Fact]
    public void Validate_RecognizesNonNullFallbacksInInsertSelectExpressions()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Id) SELECT ISNULL(source.Id, 0) FROM dbo.Source AS source; INSERT INTO dbo.Target (Id) SELECT COALESCE(source.Id, 0) FROM dbo.Source AS source; INSERT INTO dbo.Target (Id) SELECT COALESCE(source.Id, NULL) FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Id", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        var nullableFindings = findings.Where(item => item.Rule == "nullable-source-insert").ToArray();
        var finding = Assert.Single(nullableFindings);
        Assert.Contains("Id", finding.Message);
    }

    [Fact]
    public void Validate_RecognizesNonNullFirstIsNullArgument()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Id) SELECT ISNULL(source.Id, NULL) FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Id", "int", "schema.sql", 1, IsNullable: false),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "nullable-source-insert");
    }

    [Fact]
    public void Validate_RecognizesNullIfWithNullComparisonAsNonNull()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Id) SELECT NULLIF(source.Id, NULL) FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Id", "int", "schema.sql", 1, IsNullable: false),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "nullable-source-insert");
    }

    [Fact]
    public void Validate_LeavesNullIfWithValueComparisonNullable()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Id) SELECT NULLIF(source.Id, 0) FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Id", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        Assert.Contains(findings, item => item.Rule == "nullable-source-insert");
    }

    [Fact]
    public void Validate_RequiresEveryCaseInsertBranchToBeNonNull()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Id) SELECT CASE WHEN source.Id IS NULL THEN ISNULL(source.Id, 0) ELSE COALESCE(source.Id, 0) END FROM dbo.Source AS source; INSERT INTO dbo.Target (Id) SELECT CASE WHEN source.Id IS NULL THEN source.Id END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Id", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Id", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        var nullableFindings = findings.Where(item => item.Rule == "nullable-source-insert").ToArray();
        Assert.Single(nullableFindings);
    }

    [Fact]
    public void Validate_ReportsInsertSelectTypeFamilyMismatch()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Value) SELECT source.Value FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "insert-select-type");
        Assert.Contains("int", finding.Message);
        Assert.Contains("nvarchar(20)", finding.Message);
    }

    [Fact]
    public void Validate_LeavesCompatibleOrUnresolvedInsertSelectTypesUnreported()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Value) SELECT source.Value FROM dbo.Source AS source; INSERT INTO dbo.Target (Value) SELECT missing.Value FROM dbo.UnknownSource AS missing;",
            [
                new TableColumn("dbo.Target", "Value", "bigint", "schema.sql", 1),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "insert-select-type");
    }

    [Fact]
    public void Validate_ReportsInsertSelectCaseTypeFamilyMismatch()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Value) SELECT CASE WHEN source.Flag = 1 THEN 1 ELSE 2 END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Flag", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "insert-select-type");
        Assert.Contains("CASE expression", finding.Message);
        Assert.Contains("nvarchar(20)", finding.Message);
    }

    [Fact]
    public void Validate_ReportsUpdateAssignmentTypeFamilyMismatch()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = source.Value FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "update-assignment-type");
        Assert.Contains("int", finding.Message);
        Assert.Contains("nvarchar(20)", finding.Message);
    }

    [Fact]
    public void Validate_LeavesCompatibleUpdateAssignmentTypesUnreported()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = source.Value FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "bigint", "schema.sql", 1),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "update-assignment-type");
    }

    [Fact]
    public void Validate_ReportsUpdateCaseTypeFamilyMismatch()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = CASE WHEN source.Flag = 1 THEN 1 ELSE 2 END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Flag", "int", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "update-assignment-type");
        Assert.Contains("CASE expression", finding.Message);
        Assert.Contains("nvarchar(20)", finding.Message);
    }

    [Fact]
    public void Validate_LeavesCompatibleUpdateCaseTypesUnreported()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = CASE WHEN source.Flag = 1 THEN 1 ELSE 2 END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "int", "schema.sql", 1),
                new TableColumn("dbo.Source", "Flag", "int", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "update-assignment-type");
    }

    [Fact]
    public void Validate_LeavesMixedOrUnresolvedUpdateCaseTypesUnreported()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = CASE WHEN source.Flag = 1 THEN 1 ELSE source.Text END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Flag", "int", "schema.sql", 1),
                new TableColumn("dbo.Source", "Text", "nvarchar(20)", "schema.sql", 1),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "update-assignment-type");
    }

    [Fact]
    public void Validate_ReportsCastTypeFamilyMismatch()
    {
        var findings = Validate(
            "INSERT INTO dbo.Target (Value) SELECT CAST(source.Value AS INT) FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "nvarchar(20)", "schema.sql", 1),
                new TableColumn("dbo.Source", "Value", "nvarchar(20)", "schema.sql", 1),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "insert-select-type");
        Assert.Contains("CAST", finding.Message);
        Assert.Contains("nvarchar(20)", finding.Message);
    }

    [Fact]
    public void Validate_ReportsNullableUpdateAssignment()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = source.Value FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        var finding = Assert.Single(findings, item => item.Rule == "nullable-source-update");
        Assert.Contains("Value", finding.Message);
    }

    [Fact]
    public void Validate_RecognizesNonNullUpdateAssignmentExpressions()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = ISNULL(source.Value, 0) FROM dbo.Source AS source; UPDATE dbo.Target SET Value = CASE WHEN source.Value IS NULL THEN 0 ELSE 1 END FROM dbo.Source AS source;",
            [
                new TableColumn("dbo.Target", "Value", "int", "schema.sql", 1, IsNullable: false),
                new TableColumn("dbo.Source", "Value", "int", "schema.sql", 1, IsNullable: true),
            ]
        );

        Assert.DoesNotContain(findings, item => item.Rule == "nullable-source-update");
    }

    [Fact]
    public void Validate_ReportsNullUpdateAssignment()
    {
        var findings = Validate(
            "UPDATE dbo.Target SET Value = NULL;",
            [new TableColumn("dbo.Target", "Value", "int", "schema.sql", 1, IsNullable: false)]
        );

        Assert.Contains(findings, item => item.Rule == "nullability-violation");
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
