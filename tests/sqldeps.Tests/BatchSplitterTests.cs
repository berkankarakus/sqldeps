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
        Assert.Equal(8, batches[1].StartLine);
    }

    [Fact]
    public void Split_InlineSetBatchDirectives_RecoversExtractedPreamble()
    {
        var script = "SET ANSI_NULLS ON GO\nSET QUOTED_IDENTIFIER ON GO ALTER\nPROCEDURE dbo.Test AS SELECT 1;";

        var batches = BatchSplitter.Split(script);

        Assert.Collection(
            batches,
            first => Assert.Equal("SET ANSI_NULLS ON\n", first.Text),
            second => Assert.Equal("SET QUOTED_IDENTIFIER ON\n", second.Text),
            third =>
            {
                Assert.Equal(2, third.StartLine);
                Assert.Equal("ALTER\nPROCEDURE dbo.Test AS SELECT 1;\n", third.Text);
            }
        );
    }

    [Fact]
    public void Split_InlineGoInSqlStatement_DoesNotSplitTheBatch()
    {
        var batches = BatchSplitter.Split("SELECT go FROM dbo.Test WHERE value = 'GO';");

        Assert.Single(batches);
    }
}
