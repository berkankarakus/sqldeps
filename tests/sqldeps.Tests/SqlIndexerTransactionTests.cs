using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlIndexerTransactionTests : SqlFolderTestBase
{
    [Fact]
    public void Index_TransactionInsideTryCatchDoesNotReportMissingErrorHandling()
    {
        File.WriteAllText(Path.Combine(_folder, "transaction-try-catch.sql"), """
            CREATE PROCEDURE dbo.SafeTransaction
            AS
            BEGIN TRY
                BEGIN TRANSACTION;
                UPDATE dbo.Orders SET status = 1 WHERE id = 1;
                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "transaction-without-error-handling");
    }

    [Fact]
    public void Index_TransactionImmediatelyBeforeTryCatchWithRollbackDoesNotReportMissingErrorHandling()
    {
        File.WriteAllText(Path.Combine(_folder, "transaction-before-try-catch.sql"), """
            CREATE PROCEDURE dbo.SafeTransactionWithOuterBegin
            AS
            BEGIN
                BEGIN TRANSACTION;
                BEGIN TRY
                    UPDATE dbo.Orders SET status = 1 WHERE id = 1;
                    COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    ROLLBACK TRANSACTION;
                    THROW;
                END CATCH;
            END;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "transaction-without-error-handling");
    }
}
