using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Dialects;
using SqlDeps.Dialects.Tsql;

namespace SqlDeps.Tests.Dialects;

public sealed class TsqlAntlrParserTests
{
    [Fact]
    public void Parse_MapsCommonSelectReferencesToDialectAst()
    {
        const string text = "SELECT i.id FROM [dbo].[Info] AS i WITH (NOLOCK) WHERE i.id = 1;";

        var result = new TsqlAntlrParser().Parse(text);

        Assert.True(result.IsSuccess);
        var statement = Assert.Single(result.Statements);
        Assert.Equal("select", statement.Kind);
        Assert.Collection(
            statement.References,
            table =>
            {
                Assert.Equal("[dbo].[Info]", table.Name);
                Assert.Equal(SqlDialectReferenceKind.Table, table.Kind);
            },
            column =>
            {
                Assert.Equal("i.id", column.Name);
                Assert.Equal(SqlDialectReferenceKind.Column, column.Kind);
            });
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_PreservesBatchLineOffsetsAcrossGo()
    {
        const string text = "SELECT id FROM dbo.First;\nGO\n\nSELECT id FROM dbo.Second;";

        var result = new TsqlAntlrParser().Parse(text);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Statements.Count);
        Assert.Equal(3, result.Statements[1].References[0].Range.StartLine);
    }

    [Fact]
    public void Parse_ReportsInvalidSyntaxWithoutReturningPartialSuccess()
    {
        var result = new TsqlAntlrParser().Parse("SELECT FROM dbo.Info;");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Statements);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_CommonSubsetIsAcceptedByScriptDomAsWell()
    {
        const string text = "SELECT i.id FROM [dbo].[Info] AS i WITH (NOLOCK) WHERE i.id = 1;";
        using StringReader reader = new(text);
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        var result = new TsqlAntlrParser().Parse(text);

        Assert.NotNull(fragment);
        Assert.Empty(errors);
        Assert.True(result.IsSuccess);
    }
}
