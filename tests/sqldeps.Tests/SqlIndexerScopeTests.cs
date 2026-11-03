using Microsoft.Data.Sqlite;
using SqlDeps;
using SqlDeps.Analysis;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Indexing;
using SqlDeps.Storage;

namespace SqlDeps.Tests;

public sealed class SqlIndexerScopeTests : SqlFolderTestBase
{
    [Fact]
    public void Index_GroupedAggregate_DoesNotReportAggregateArgumentAsUngrouped()
    {
        File.WriteAllText(Path.Combine(_folder, "grouped-aggregate.sql"), """
            CREATE FUNCTION dbo.GetInvalidClicks()
            RETURNS TABLE
            AS
            RETURN
            (
                SELECT clicks.AtDate, SUM(clicks.InvalidClick) AS InvalidClicks
                FROM dbo.Clicks clicks
                GROUP BY clicks.AtDate
            );
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "ungrouped-select-column");
    }

    [Fact]
    public void Index_NestedQueryScopesAllowTheSameTableAlias()
    {
        File.WriteAllText(Path.Combine(_folder, "nested-alias.sql"), """
            CREATE PROCEDURE dbo.GetItems
            AS
            SELECT outerInfo.id
            FROM (SELECT innerInfo.id FROM dbo.Info innerInfo) outerInfo
            JOIN dbo.OtherInfo innerInfo ON innerInfo.id = outerInfo.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "duplicate-alias");
    }

    [Fact]
    public void Index_CrossApplyFunctionAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "cross-apply.sql"), """
            CREATE PROCEDURE dbo.GetSplitItems
            AS
            SELECT splitItem.value
            FROM dbo.Info info
            CROSS APPLY dbo.SplitValues(info.Tags) splitItem;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_InlineValuesAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "inline-values.sql"), """
            CREATE PROCEDURE dbo.GetValues
            AS
            SELECT valuesRow.itemID
            FROM (VALUES (1), (2)) valuesRow(itemID);
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_ContainsTableAliasIsKnownInTheQueryScope()
    {
        File.WriteAllText(Path.Combine(_folder, "contains-table.sql"), """
            CREATE PROCEDURE dbo.GetFullTextItems
            AS
            SELECT fullText.[KEY]
            FROM CONTAINSTABLE(dbo.Info, *, 'value') fullText;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "unbound-identifier");
    }

    [Fact]
    public void Index_SelectStarFromTemporaryTable_UsesItsKnownColumnCountInUnion()
    {
        File.WriteAllText(Path.Combine(_folder, "temporary-union.sql"), """
            CREATE PROCEDURE dbo.GetTemporaryItems
            AS
            SELECT id, title, status
            INTO #items
            FROM dbo.Info;

            SELECT * FROM #items
            UNION ALL
            SELECT id, title, status FROM dbo.Info;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "set-operation-column-count");
    }

    [Fact]
    public void Index_LocalDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "local-dml.sql"), """
            CREATE PROCEDURE dbo.ClearLocalRows
            AS
            BEGIN
                DECLARE @rows TABLE (id INT);
                CREATE TABLE #rows (id INT);
                UPDATE @rows SET id = 1;
                DELETE FROM @rows;
                UPDATE rowsAlias
                SET id = 1
                FROM @rows AS rowsAlias;
                DELETE rowsAlias
                FROM @rows AS rowsAlias;
                UPDATE #rows SET id = 1;
                DELETE FROM #rows;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_AliasedLocalDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "aliased-local-dml.sql"), """
            CREATE PROCEDURE dbo.ClearAliasedLocalRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                UPDATE rowsAlias
                SET id = 1
                FROM #rows AS rowsAlias;
                DELETE rowsAlias
                FROM #rows AS rowsAlias;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_DmlJoinPredicateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-join-filter.sql"), """
            UPDATE target
            SET status = 1
            FROM dbo.Orders target
            JOIN dbo.OrderKeys keys ON keys.id = target.id;

            DELETE target
            FROM dbo.Orders target
            JOIN dbo.OrderKeys keys ON keys.id = target.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_LimitedDmlTargetsDoNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "limited-dml.sql"), """
            UPDATE TOP (10) dbo.Orders SET status = 1;
            DELETE TOP (10) FROM dbo.Orders;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule is "update-without-where" or "delete-without-where");
    }

    [Fact]
    public void Index_TopLimitedCteDeleteDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "top-limited-cte-delete.sql"), """
            CREATE PROCEDURE dbo.TrimLocalRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                WITH limited AS
                (
                    SELECT TOP (1) id
                    FROM #rows
                    ORDER BY id
                )
                DELETE FROM limited;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "delete-without-where");
    }

    [Fact]
    public void Index_FilteredCteUpdateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "filtered-cte-update.sql"), """
            CREATE PROCEDURE dbo.UpdateFilteredRows
            AS
            BEGIN
                CREATE TABLE #rows (id INT);
                WITH filtered AS
                (
                    SELECT id
                    FROM #rows
                    WHERE id > 0
                )
                UPDATE filtered SET id = id + 1;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "update-without-where");
    }

    [Fact]
    public void Index_RestrictedDerivedUpdateDoesNotReportMissingWhere()
    {
        File.WriteAllText(Path.Combine(_folder, "restricted-derived-update.sql"), """
            CREATE PROCEDURE dbo.UpdateOneRow
            AS
            BEGIN
                UPDATE selected
                SET status = 1
                FROM
                (
                    SELECT TOP (1) id, status
                    FROM dbo.Orders
                    WHERE status = 0
                    ORDER BY id
                ) AS selected;
            END;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "update-without-where");
    }

    [Fact]
    public void Index_NestedDerivedAliasDoesNotShadowDmlTarget()
    {
        File.WriteAllText(Path.Combine(_folder, "nested-derived-alias.sql"), """
            UPDATE target
            SET status = 1
            FROM dbo.Orders AS target
            JOIN
            (
                SELECT id
                FROM
                (
                    SELECT id
                    FROM dbo.OtherOrders AS target
                ) AS target
            ) AS source ON source.id = target.id;
            """);
        var indexer = new SqlFolderIndexer(ObjectNames.StandardDefaultSchema, [], jobs: 1);

        indexer.Index(_folder);

        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule == "dml-derived-target");
    }
}
