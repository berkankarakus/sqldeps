using Microsoft.Data.Sqlite;
using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Indexing;
using SqlDeps.Storage;

namespace SqlDeps.Tests;

public sealed class SqlIndexerLintTests : SqlFolderTestBase
{
    [Fact]
    public void Index_MergeTopSourceRequiresAnOrderBy()
    {
        File.WriteAllText(Path.Combine(_folder, "merge-top-order.sql"), """
            MERGE dbo.Orders AS target
            USING (SELECT TOP (1) id FROM dbo.NewOrders) AS source
            ON target.id = source.id
            WHEN MATCHED THEN UPDATE SET status = 1;

            MERGE dbo.Orders AS orderedTarget
            USING (SELECT TOP (1) id FROM dbo.NewOrders ORDER BY id) AS orderedSource
            ON orderedTarget.id = orderedSource.id
            WHEN MATCHED THEN UPDATE SET status = 1;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var findings = indexer.LintFindings.Where(finding => finding.Rule == "merge-top-without-order").ToArray();
        Assert.Single(findings);
        Assert.Equal(2, findings[0].Line);
    }

    [Fact]
    public void Index_InsertOnlyMergeWithConstantFalseMatchDoesNotReportNondeterministicMatch()
    {
        File.WriteAllText(Path.Combine(_folder, "merge-insert-only.sql"), """
            MERGE dbo.Orders AS target
            USING dbo.NewOrders AS source
            ON 1 = 0
            WHEN NOT MATCHED THEN INSERT (id) VALUES (source.id);
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge-nondeterministic-match");
    }

    [Fact]
    public void Index_DmlConstantCorpus_KeepsOnlyCollationIndependentStringComparisons()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-constant-corpus.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE '' = '';
            DELETE FROM dbo.Orders WHERE 'a' = 'A';
            UPDATE dbo.Orders SET status = 1 WHERE 'same' <> 'same';
            DELETE FROM dbo.Orders WHERE 1.25 < 1.25;
            UPDATE dbo.Orders SET status = 1 WHERE 1.25 >= 1.25;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule.StartsWith("dml-", StringComparison.Ordinal))
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 3, 4, 5], findings.Select(finding => finding.Line).ToArray());
        Assert.Equal(
            ["dml-ineffective-filter", "dml-always-false-filter", "dml-always-false-filter", "dml-ineffective-filter"],
            findings.Select(finding => finding.Rule).ToArray());
        Assert.All(findings, finding =>
        {
            Assert.Equal("info", finding.Severity);
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }

    [Fact]
    public void Index_FilteredIndex_PreservesPredicateAndColumnCollationMetadata()
    {
        File.WriteAllText(Path.Combine(_folder, "filtered-index.sql"), """
            CREATE TABLE dbo.FilteredText (id INT, status NVARCHAR(20) COLLATE Latin1_General_100_CI_AS);
            CREATE INDEX IX_FilteredText_Id ON dbo.FilteredText (id) WHERE status = N'active';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        var index = Assert.Single(indexer.IndexColumns);
        Assert.Equal("status = N'active'", index.FilterPredicate, ignoreCase: true);
        var column = Assert.Single(indexer.TableColumns, item => item.ColumnName == "status");
        Assert.Equal("nvarchar (20)", column.DataType, ignoreCase: true);
        Assert.Equal("Latin1_General_100_CI_AS", column.Collation);
    }

    [Fact]
    public void Index_SuppressesOnlyTheRuleOnTheNextLine()
    {
        File.WriteAllText(Path.Combine(_folder, "suppressed.sql"), """
            -- sqldeps: disable-next-line=merge
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.Id = source.Id
            WHEN MATCHED THEN UPDATE SET Status = 'Archived';
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge");
    }
}
