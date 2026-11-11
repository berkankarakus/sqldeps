using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;

namespace SqlDeps.Tests.Indexing;

public sealed class FindersTests
{
    [Fact]
    public void CteNameCollector_ReturnsCteNamesWithoutCaseSensitivity()
    {
        var script = Parse("WITH ActiveRows AS (SELECT 1 AS Id) SELECT Id FROM activerows;");

        var names = CteNameCollector.Collect(script);

        Assert.Contains("ActiveRows", names);
        Assert.Contains("activerows", names);
    }

    [Fact]
    public void QuerySpecificationFinder_ReturnsOuterAndNestedQueries()
    {
        var script = Parse("SELECT u.Id FROM dbo.Users AS u WHERE EXISTS (SELECT 1 FROM dbo.Logins AS l WHERE l.UserId = u.Id);");

        var queries = QuerySpecificationFinder.Find(script);

        Assert.Equal(2, queries.Count);
    }

    [Fact]
    public void TableReferenceFinder_ReturnsNamedTablesAcrossNestedQueries()
    {
        var script = Parse("SELECT u.Id FROM dbo.Users AS u WHERE EXISTS (SELECT 1 FROM dbo.Logins AS l WHERE l.UserId = u.Id);");

        var references = TableReferenceFinder.Find(script);

        Assert.Equal(["Users", "Logins"], references.Select(reference => reference.SchemaObject.BaseIdentifier.Value));
    }

    [Fact]
    public void DirectQueryDerivedTableFinder_ReturnsOnlyOutermostDerivedTable()
    {
        var script = Parse(
            "SELECT d.Id FROM (SELECT inner_d.Id FROM (SELECT Id FROM dbo.Users) AS inner_d) AS d;"
        );

        var query = QuerySpecificationFinder.Find(script).First();
        var derivedTables = DirectQueryDerivedTableFinder.Find(query.FromClause);

        Assert.Single(derivedTables);
        Assert.Equal("d", derivedTables[0].Alias?.Value);
    }

    [Fact]
    public void DirectTableAliasFinder_ExcludesNestedAliasesFromOuterScope()
    {
        var script = Parse(
            "SELECT d.Id FROM dbo.Users AS u JOIN (SELECT Id FROM dbo.Logins AS l) AS d ON d.Id = u.Id;"
        );

        var query = QuerySpecificationFinder.Find(script).First();
        var scope = DirectTableAliasFinder.FindScope(query.FromClause);

        Assert.Equal(["u", "d"], scope.Aliases.Select(reference => reference.Alias?.Value));
        Assert.Single(scope.Tables);
        Assert.Equal("Users", scope.Tables[0].SchemaObject.BaseIdentifier.Value);
    }

    [Fact]
    public void VariableTableReferenceFinder_FindsTableVariables()
    {
        var script = Parse("SELECT item.Id FROM @items AS item;");

        var references = VariableTableReferenceFinder.Find(script);

        Assert.Single(references);
        Assert.Equal("item", references[0].Alias?.Value);
    }

    [Fact]
    public void QueryDerivedTableFinder_ReturnsNestedDerivedTables()
    {
        var script = Parse(
            "SELECT d.Id FROM (SELECT inner_d.Id FROM (SELECT Id FROM dbo.Users) AS inner_d) AS d;"
        );

        var query = QuerySpecificationFinder.Find(script).First();
        var derivedTables = QueryDerivedTableFinder.Find(query.FromClause);

        Assert.Equal(2, derivedTables.Count);
    }

    [Fact]
    public void TransactionFinders_RecognizeTryCatchAndRollbackStatements()
    {
        var script = Parse(
            "BEGIN TRY BEGIN TRANSACTION; END TRY BEGIN CATCH ROLLBACK TRANSACTION; END CATCH;"
        );

        var tryCatchStatements = TryCatchStatementFinder.Find(script);
        RollbackTransactionFinder rollbackFinder = new();
        script.Accept(rollbackFinder);

        Assert.Single(tryCatchStatements);
        Assert.True(rollbackFinder.Found);
    }

    [Fact]
    public void FinderMethods_ReturnEmptyResultsForNullFragments()
    {
        Assert.Empty(QuerySpecificationFinder.Find(null));
        Assert.Empty(TableReferenceFinder.Find(null));
        Assert.Empty(VariableTableReferenceFinder.Find(null));
        Assert.Empty(QueryDerivedTableFinder.Find(null));
        Assert.Empty(DirectQueryDerivedTableFinder.Find(null));
        Assert.Empty(DirectTableAliasFinder.Find(null));
    }

    private static TSqlScript Parse(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        Assert.Empty(errors);

        return Assert.IsType<TSqlScript>(fragment);
    }
}
