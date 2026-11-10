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
}
