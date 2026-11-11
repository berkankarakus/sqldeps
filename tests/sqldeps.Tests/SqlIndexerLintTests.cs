using Microsoft.Data.Sqlite;
using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
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
        var indexer = CreateIndexer();

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
        var indexer = CreateIndexer();

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
        var indexer = CreateIndexer();

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
    public void Index_SelectConstantFalseFilter_ReportsDeadQueryWithoutReportingUnfilteredSelect()
    {
        File.WriteAllText(Path.Combine(_folder, "select-false-filters.sql"), """
            SELECT id FROM dbo.Orders WHERE 7 > 10;
            SELECT id FROM dbo.Orders WHERE 4 BETWEEN 1 AND 3;
            SELECT id FROM dbo.Orders WHERE 7 IN (1, 2, 3);
            SELECT id FROM dbo.Orders WHERE 'same' <> 'same';
            SELECT id FROM dbo.Orders WHERE 2 + 3 = 6;
            SELECT id FROM dbo.Orders WHERE 4 * 2 < 7;
            SELECT id FROM dbo.Orders WHERE 10 / 2 = 6;
            SELECT id FROM dbo.Orders WHERE 10 % 3 = 0;
            SELECT id FROM dbo.Orders GROUP BY id HAVING 2 + 2 = 5;
            SELECT id FROM dbo.Orders WHERE id = @id;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "select-always-false-filter")
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], findings.Select(finding => finding.Line).ToArray());
        Assert.Contains(findings, finding => finding.Message.Contains("HAVING condition is always false"));
        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unfiltered-select");
        Assert.All(findings, finding =>
        {
            Assert.Equal("info", finding.Severity);
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }

    [Fact]
    public void Index_SelectConstantTrueFilter_ReportsReviewOnlyFinding()
    {
        File.WriteAllText(Path.Combine(_folder, "select-true-filters.sql"), """
            SELECT id FROM dbo.Orders WHERE 1 = 1;
            SELECT id FROM dbo.Orders GROUP BY id HAVING 2 + 2 = 4;
            SELECT id FROM dbo.Orders WHERE id = @id;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "select-ineffective-filter")
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 2], findings.Select(finding => finding.Line).ToArray());
        Assert.Contains(findings, finding => finding.Message.Contains("HAVING condition is always true"));
        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Line == 3 && finding.Rule == "select-ineffective-filter");
        Assert.All(findings, finding =>
        {
            Assert.Equal("info", finding.Severity);
            Assert.True(finding.Column > 0);
            Assert.True(finding.Length > 0);
        });
    }

    [Fact]
    public void Index_RecursiveCteWithoutVisibleGuard_ReportsReviewOnlyFinding()
    {
        File.WriteAllText(Path.Combine(_folder, "recursive-cte.sql"), """
            WITH Numbers AS
            (
                SELECT 1 AS n
                UNION ALL
                SELECT n + 1 FROM Numbers
            )
            SELECT n FROM Numbers;

            WITH GuardedNumbers AS
            (
                SELECT 1 AS n
                UNION ALL
                SELECT n + 1 FROM GuardedNumbers WHERE n < 10
            )
            SELECT n FROM GuardedNumbers;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "recursive-cte")
            .ToArray();

        var finding = Assert.Single(findings);
        Assert.Equal("info", finding.Severity);
        Assert.Contains("Numbers", finding.Message);
        Assert.Contains("termination", finding.Message);
        Assert.DoesNotContain(findings, item => item.Message.Contains("GuardedNumbers"));
    }

    [Fact]
    public void Index_DuplicateLintGeneration_IsExposedOnlyOnce()
    {
        File.WriteAllText(Path.Combine(_folder, "duplicate-lint.sql"), """
            SELECT id FROM dbo.Orders WITH (NOLOCK);
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "nolock")
            .ToArray();
        Assert.Single(findings);
    }

    [Fact]
    public void Index_ConstantIfConditions_ReportOnlyUnreachableBranches()
    {
        File.WriteAllText(Path.Combine(_folder, "constant-if.sql"), """
            IF 2 + 3 = 6
            BEGIN
                SELECT 1;
            END;

            IF 4 * 2 = 8
            BEGIN
                SELECT 1;
            END
            ELSE
            BEGIN
                SELECT 2;
            END;

            IF @enabled = 1
            BEGIN
                SELECT 3;
            END;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "unreachable-branch")
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([2, 11], findings.Select(finding => finding.Line).ToArray());
        Assert.All(findings, finding => Assert.Equal("info", finding.Severity));
    }

    [Fact]
    public void Index_DynamicSql_DistinguishesConstantAndConcatenatedForms()
    {
        File.WriteAllText(Path.Combine(_folder, "dynamic-sql-forms.sql"), """
            EXEC(N'SELECT Id FROM dbo.Orders;');
            EXEC(N'SELECT Id FROM dbo.Orders WHERE Id = ' + @id);
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.Contains(indexer.Dependencies, dependency =>
            dependency.TargetObject == "dbo.Orders" && dependency.ViaDynamic);

        var finding = Assert.Single(indexer.LintFindings, item => item.Rule == "dynamic-sql-concatenation");
        Assert.Equal("warning", finding.Severity);
        Assert.Equal(2, finding.Line);
        Assert.True(finding.Column > 0);
        Assert.True(finding.Length > 0);
    }

    [Fact]
    public void Index_PreservesDistinctSameRuleAndLineFindings()
    {
        File.WriteAllText(Path.Combine(_folder, "duplicate-lint.sql"), "SELECT * FROM dbo.Orders WHERE name LIKE '%first%' AND title LIKE '%second%';");
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings
            .Where(finding => finding.Rule == "leading-wildcard-like")
            .ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.Equal(1, finding.Line));
        Assert.NotEqual(findings[0].Column, findings[1].Column);
    }

    [Fact]
    public void Index_FilteredIndex_PreservesPredicateAndColumnCollationMetadata()
    {
        File.WriteAllText(Path.Combine(_folder, "filtered-index.sql"), """
            CREATE TABLE dbo.FilteredText (id INT, status NVARCHAR(20) COLLATE Latin1_General_100_CI_AS);
            CREATE INDEX IX_FilteredText_Id ON dbo.FilteredText (id) WHERE status = N'active';
            """);
        var indexer = CreateIndexer();

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
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "merge");
    }
}
