using SqlDeps;

namespace SqlDeps.Tests;

public sealed class SqlIndexerDmlConditionTests : SqlFolderTestBase
{
    [Fact]
    public void Index_DmlIneffectiveFilter_SuppressesUnstableConditions()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-filters.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND @enabled = 1;
            DELETE FROM dbo.Orders WHERE 1 = 1 AND GETDATE() = GETDATE();
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND id = 1;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var findings = indexer.LintFindings.Where(finding => finding.Rule == "dml-ineffective-filter").ToArray();
        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void Index_DmlConditionFindings_CoverUpdateDeleteAndMerge()
    {
        File.WriteAllText(Path.Combine(_folder, "dml-condition-families.sql"), """
            UPDATE dbo.Orders SET status = 1 WHERE 1 = 1 AND id = 1;
            DELETE FROM dbo.Orders WHERE 1 = 1 AND id = 1;
            MERGE dbo.Orders AS target USING dbo.Orders AS source ON target.id = source.id
            WHEN MATCHED THEN UPDATE SET status = 1;
            """);
        var indexer = CreateIndexer();

        indexer.Index(_folder);

        var ineffective = indexer.LintFindings
            .Where(finding => finding.Rule == "dml-ineffective-filter")
            .OrderBy(finding => finding.Line)
            .ToArray();
        Assert.Equal([1, 2], ineffective.Select(finding => finding.Line).ToArray());
        Assert.Contains(indexer.LintFindings, finding => finding.Rule == "merge" && finding.Line == 3);
        Assert.DoesNotContain(indexer.LintFindings, finding => finding.Rule.StartsWith("dml-", StringComparison.Ordinal) && finding.Line >= 3);
    }
}
