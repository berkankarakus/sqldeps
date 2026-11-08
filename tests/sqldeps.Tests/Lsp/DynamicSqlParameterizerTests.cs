using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class DynamicSqlParameterizerTests
{
    [Fact]
    public void CoreService_DelegatesSafeParameterization()
    {
        var success = DynamicSqlParameterizationService.TryParameterize(
            "'SELECT * FROM dbo.Products WHERE id = ' + @id",
            "@id int",
            out var replacement);

        Assert.True(success);
        Assert.Equal(
            "N'SELECT * FROM dbo.Products WHERE id = @__sqldeps_0', N'@__sqldeps_0 int', @__sqldeps_0 = @id",
            replacement);
    }

    [Fact]
    public void TryParameterize_ConvertsDataValueConcatenationToSpExecuteSqlArguments()
    {
        var success = DynamicSqlParameterizer.TryParameterize(
            "N'SELECT * FROM dbo.Info WHERE id = ' + @id",
            "DECLARE @id INT = 1;",
            out var replacement
        );

        Assert.True(success);
        Assert.Equal(
            "N'SELECT * FROM dbo.Info WHERE id = @__sqldeps_0', N'@__sqldeps_0 INT', @__sqldeps_0 = @id",
            replacement
        );
    }

    [Fact]
    public void TryParameterize_ReusesOneGeneratedParameterForRepeatedVariable()
    {
        var success = DynamicSqlParameterizer.TryParameterize(
            "N'SELECT * FROM dbo.Info WHERE id = ' + @id + N' OR parent_id = ' + @id",
            "DECLARE @id INT = 1;",
            out var replacement
        );

        Assert.True(success);
        Assert.Equal(
            "N'SELECT * FROM dbo.Info WHERE id = @__sqldeps_0 OR parent_id = @__sqldeps_0', N'@__sqldeps_0 INT', @__sqldeps_0 = @id",
            replacement
        );
        Assert.DoesNotContain("@__sqldeps_1", replacement);
    }

    [Fact]
    public void TryParameterize_RejectsDynamicObjectNames()
    {
        var success = DynamicSqlParameterizer.TryParameterize(
            "N'SELECT * FROM ' + @tableName",
            "DECLARE @tableName NVARCHAR(128);",
            out var replacement
        );

        Assert.False(success);
        Assert.Empty(replacement);
    }

    [Fact]
    public void TryParameterize_RejectsUnsupportedVariableTypes()
    {
        var success = DynamicSqlParameterizer.TryParameterize(
            "N'SELECT * FROM dbo.Info WHERE id = ' + @id",
            "DECLARE @id SQL_VARIANT;",
            out var replacement
        );

        Assert.False(success);
        Assert.Empty(replacement);
    }

    [Fact]
    public void TryParameterize_RejectsGeneratedParameterNameCollisions()
    {
        var success = DynamicSqlParameterizer.TryParameterize(
            "N'SELECT * FROM dbo.Info WHERE id = ' + @id + N' AND marker = @__sqldeps_0'",
            "DECLARE @id INT = 1;",
            out var replacement
        );

        Assert.False(success);
        Assert.Empty(replacement);
    }
}
