using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps;

public sealed partial class SqlFolderIndexer
{
    private void IndexBatches(
        string sourceText,
        string relativePath,
        IndexedFileContribution result,
        string repositoryRoot)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);

        IReadOnlyList<SqlBatch> batches = BatchSplitter.Split(sourceText);
        IReadOnlyList<CachedBatch> cachedBatches = batchCache?.GetValueOrDefault(relativePath) ?? [];
        Dictionary<int, CachedBatch> cachedBatchesByOrdinal = CreateBatchLookup(cachedBatches);

        for (int ordinal = 0; ordinal < batches.Count; ordinal++)
        {
            SqlBatch batch = batches[ordinal];
            IndexedFileContribution batchResult = new(result.File);
            IndexBatchContribution(
                batch,
                ordinal,
                relativePath,
                parser,
                sourceText,
                repositoryRoot,
                cachedBatchesByOrdinal,
                batchResult);

            result.Merge(batchResult);
            result.Batches.Add(new CachedBatch(
                relativePath,
                ordinal,
                batch.StartLine,
                BatchContributionCache.HashOf(batch.Text),
                BatchContributionCache.Serialize(batchResult)));
        }
    }

    private void IndexBatchContribution(
        SqlBatch batch,
        int ordinal,
        string relativePath,
        TSql160Parser parser,
        string sourceText,
        string repositoryRoot,
        IReadOnlyDictionary<int, CachedBatch> cachedBatches,
        IndexedFileContribution result)
    {
        if (batch.Text.Length > MaxBatchCharacters)
        {
            result.Problems.Add(new ParseProblem(
                relativePath,
                batch.StartLine,
                $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."));

            return;
        }

        if (TryReuseBatch(cachedBatches, ordinal, batch, result.File, result))
        {
            result.ReusedBatchCount++;

            return;
        }

        IndexBatch(batch, relativePath, parser, result, sourceText, repositoryRoot);
        result.ParsedBatchCount++;
    }

    private static Dictionary<int, CachedBatch> CreateBatchLookup(IReadOnlyList<CachedBatch> cachedBatches)
    {
        Dictionary<int, CachedBatch> batchesByOrdinal = [];

        foreach (CachedBatch cachedBatch in cachedBatches)
        {
            batchesByOrdinal[cachedBatch.Ordinal] = cachedBatch;
        }

        return batchesByOrdinal;
    }

    private static bool TryReuseBatch(
        IReadOnlyDictionary<int, CachedBatch> cachedBatches,
        int ordinal,
        SqlBatch batch,
        IndexedFile file,
        IndexedFileContribution result)
    {
        if (!cachedBatches.TryGetValue(ordinal, out CachedBatch? cached))
        {
            return false;
        }

        if (cached is null)
        {
            return false;
        }

        if (!BatchContributionCache.Matches(cached, file.Path, ordinal, batch.StartLine, batch.Text))
        {
            return false;
        }

        if (!BatchContributionCache.TryDeserialize(cached, file, out IndexedFileContribution? cachedResult))
        {
            return false;
        }

        if (cachedResult is null)
        {
            return false;
        }

        result.Merge(cachedResult);

        return true;
    }

    private static IndexedFileContribution OversizedFile(string relativePath)
    {
        IndexedFileContribution result = new(new IndexedFile(relativePath, ""));
        result.Problems.Add(new ParseProblem(relativePath, 1, $"SQL file exceeds the {MaxFileBytes / (1024 * 1024)} MiB limit."));

        return result;
    }
}
