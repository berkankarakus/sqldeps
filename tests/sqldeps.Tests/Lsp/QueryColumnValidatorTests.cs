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
            []
        ).ToArray();
    }
}
