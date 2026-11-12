namespace SqlDeps.Tests;

public sealed class SqlIndexerRecoveryTests : SqlFolderTestBase
{
    [Fact]
    public void Index_MalformedProcedureReportsParseProblemsWithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_folder, "malformed-procedure.sql"), """
            CREATE PROCEDURE dbo.Broken
            AS
            BEGIN
                SELECT FROM;
            END;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.NotEmpty(indexer.Problems);
        Assert.Contains(indexer.Problems, problem => problem.SourceFile == "malformed-procedure.sql");
    }

    [Fact]
    public void Index_UserScriptPreservesValidPrefixBeforeParseError()
    {
        var userScriptsFolder = Path.Combine(_folder, "UserScripts");
        Directory.CreateDirectory(userScriptsFolder);
        File.WriteAllText(
            Path.Combine(userScriptsFolder, "partial.sql"),
            "CREATE TABLE dbo.Known (Id INT);\nSELECT FROM;"
        );
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.Objects, item => item.Name == "dbo.Known" && item.Kind == "table");
        Assert.Contains(indexer.Warnings, warning => warning.SourceFile == "UserScripts/partial.sql");
        Assert.DoesNotContain(indexer.Problems, problem => problem.SourceFile == "UserScripts/partial.sql");
    }
}
