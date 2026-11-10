using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps;

public sealed partial class SqlFolderIndexer
{
    private IndexedFileContribution IndexFile(
        string path,
        string relativePath,
        string repositoryRoot
    )
    {
        try
        {
            return IndexFileCore(path, relativePath, repositoryRoot);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return FailedFile(relativePath, exception);
        }
    }

    private IndexedFileContribution IndexFileCore(
        string path,
        string relativePath,
        string repositoryRoot
    )
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            return OversizedFile(relativePath);
        }

        var bytes = File.ReadAllBytes(path);
        var hash = SqlFileReader.HashOf(bytes);
        var indexedFile = new IndexedFile(relativePath, hash);

        if (TryGetCachedContribution(relativePath, indexedFile, hash, out var cachedContribution))
        {
            return cachedContribution;
        }

        IndexedFileContribution result = new(indexedFile);
        var sourceText = SqlFileReader.Decode(bytes);
        IndexBatches(sourceText, relativePath, result, repositoryRoot);

        return result;
    }

    private static IndexedFileContribution FailedFile(string relativePath, Exception exception)
    {
        IndexedFileContribution result = new(new IndexedFile(relativePath, ""));
        result.Warnings.Add(new IndexWarning(
            relativePath,
            1,
            $"File could not be indexed ({exception.GetType().Name}): {exception.Message}"));

        return result;
    }

    private bool TryGetCachedContribution(
        string relativePath,
        IndexedFile indexedFile,
        string hash,
        out IndexedFileContribution contribution)
    {
        contribution = null!;

        if (!cache.TryGetValue(relativePath, out var cached) || cached.Hash != hash)
        {
            return false;
        }

        contribution = IndexedFileContribution.FromCache(indexedFile, cached);
        RestoreCachedBatches(relativePath, contribution);

        return true;
    }

    private void RestoreCachedBatches(string relativePath, IndexedFileContribution contribution)
    {
        if (batchCache is null)
        {
            return;
        }

        if (!batchCache.TryGetValue(relativePath, out List<CachedBatch>? cachedBatches))
        {
            return;
        }

        contribution.Batches.AddRange(cachedBatches);
    }
}
