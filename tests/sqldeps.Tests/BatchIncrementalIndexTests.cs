using SqlDeps;
using SqlDeps.Domain;

namespace SqlDeps.Tests;

public sealed class BatchIncrementalIndexTests : SqlFolderTestBase
{
    [Fact]
    public void ChangedBatchKeepsUnchangedBatchContribution()
    {
        const string relativePath = "script.sql";
        string source = "CREATE TABLE dbo.FirstTable (id int);\nGO\nCREATE TABLE dbo.SecondTable (id int);\n";
        File.WriteAllText(Path.Combine(_folder, relativePath), source);

        SqlFolderIndexer first = CreateIndexer();
        first.Index(_folder);
        Dictionary<string, List<CachedBatch>> batchCache = first.Batches
            .GroupBy(batch => batch.SourceFile, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        string changedSource = source.Replace("SecondTable", "ThirdTable", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(_folder, relativePath), changedSource);

        SqlFolderIndexer second = new(
            ObjectNames.StandardDefaultSchema,
            [],
            jobs: 1,
            batchCache: batchCache);
        second.Index(_folder);

        Assert.Equal(2, second.Batches.Count);
        Assert.Equal(first.Batches[0].Payload, second.Batches[0].Payload);
        Assert.Contains(second.Objects, item => item.Name == "dbo.FirstTable");
        Assert.Contains(second.Objects, item => item.Name == "dbo.ThirdTable");
        Assert.DoesNotContain(second.Objects, item => item.Name == "dbo.SecondTable");
    }
}
