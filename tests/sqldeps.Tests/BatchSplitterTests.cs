using SqlDeps;

namespace SqlDeps.Tests;

public sealed class BatchSplitterTests
{
    [Fact]
    public void Split_GoDirectives_PreservesBatchesAndSourceLines()
    {
        var batches = BatchSplitter.Split("SELECT 1;\nGO 2\n\nSELECT 2;\nGO -- separator\n");

        Assert.Collection(
            batches,
            first =>
            {
                Assert.Equal(1, first.StartLine);
                Assert.Equal("SELECT 1;\n", first.Text);
            },
            second =>
            {
                Assert.Equal(3, second.StartLine);
                Assert.Equal("\nSELECT 2;\n", second.Text);
            }
        );
    }

    [Fact]
    public void Split_OnlyDirectives_ProducesNoEmptyBatches()
    {
        var batches = BatchSplitter.Split("GO\n  GO 4 -- repeat\n");

        Assert.Empty(batches);
    }

    [Fact]
    public void Split_GoInsideStringOrComment_DoesNotSplitTheBatch()
    {
        var script = "EXEC msdb.dbo.sp_add_jobstep @command = N'USE app;\nGO\nSELECT 1;';\n/*\nGO\n*/\nGO\nSELECT 2;";

        var batches = BatchSplitter.Split(script);

        Assert.Equal(2, batches.Count);
        Assert.Contains("GO\nSELECT 1", batches[0].Text);
        Assert.Equal(7, batches[1].StartLine);
    }
}
