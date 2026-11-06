using SqlDeps;

namespace SqlDeps.Tests;

public sealed class BatchContributionCacheTests
{
    [Fact]
    public void HashOf_IsStableForEquivalentBatchText()
    {
        string first = BatchContributionCache.HashOf("SELECT 1;\n");
        string repeated = BatchContributionCache.HashOf("SELECT 1;\n");
        string changed = BatchContributionCache.HashOf("SELECT 2;\n");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, changed);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesContributionModels()
    {
        IndexedFile file = new("batch.sql", "file-hash");
        IndexedFileContribution original = new(file);
        original.Objects.Add(new DatabaseObject("dbo.Items", "table", "batch.sql", 1));
        original.Dependencies.Add(new Dependency("(batch)", "dbo.Items", "SELECT", "batch.sql", 2));
        original.LintFindings.Add(new LintFinding(
            "select-star",
            "info",
            "(batch)",
            "batch.sql",
            2,
            1,
            1,
            "SELECT * makes result shape harder to reason about."));

        CachedBatch batch = new(
            "batch.sql",
            0,
            1,
            BatchContributionCache.HashOf("SELECT * FROM dbo.Items;\n"),
            BatchContributionCache.Serialize(original));
        IndexedFileContribution restored = BatchContributionCache.Deserialize(batch, file);

        Assert.Equal(original.Objects, restored.Objects);
        Assert.Equal(original.Dependencies, restored.Dependencies);
        Assert.Equal(original.LintFindings, restored.LintFindings);
        Assert.True(restored.WasReused);
        Assert.Equal(file, restored.File);
    }

    [Fact]
    public void Matches_RequiresStableBatchIdentity()
    {
        CachedBatch batch = new("batch.sql", 2, 8, BatchContributionCache.HashOf("SELECT 1;"), "{}");

        Assert.True(BatchContributionCache.Matches(batch, "batch.sql", 2, 8, "SELECT 1;"));
        Assert.False(BatchContributionCache.Matches(batch, "batch.sql", 3, 8, "SELECT 1;"));
        Assert.False(BatchContributionCache.Matches(batch, "batch.sql", 2, 9, "SELECT 1;"));
        Assert.False(BatchContributionCache.Matches(batch, "batch.sql", 2, 8, "SELECT 2;"));
    }

    [Fact]
    public void TryDeserialize_ReturnsFalseForMalformedPayload()
    {
        CachedBatch batch = new("batch.sql", 0, 1, "hash", "not-json");

        bool reused = BatchContributionCache.TryDeserialize(
            batch,
            new IndexedFile("batch.sql", "file-hash"),
            out IndexedFileContribution? contribution);

        Assert.False(reused);
        Assert.Null(contribution);
    }
}
