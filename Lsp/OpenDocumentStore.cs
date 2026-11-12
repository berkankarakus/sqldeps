using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

/// <summary>Stores open-document analyses and rejects stale LSP document versions.</summary>
internal sealed class OpenDocumentStore
{
    private readonly Dictionary<string, AnalysisResult> documents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> revisions = new(StringComparer.Ordinal);

    internal IReadOnlyList<AnalysisResult> Values => documents.Values.ToArray();

    internal IReadOnlyList<OpenDocumentSnapshot> Snapshots()
    {
        return documents
            .Select(pair => new OpenDocumentSnapshot(pair.Key, pair.Value, revisions[pair.Key]))
            .ToArray();
    }

    internal AnalysisResult? Get(string uri)
    {
        return documents.GetValueOrDefault(uri);
    }

    internal void Remove(string uri)
    {
        documents.Remove(uri);
        versions.Remove(uri);
        revisions.Remove(uri);
    }

    internal int Revision(string uri)
    {
        return revisions.GetValueOrDefault(uri);
    }

    internal bool Store(
        string uri,
        AnalysisResult analysis,
        int? version,
        bool allowSameVersion
    )
    {
        if (IsStale(uri, version, allowSameVersion))
        {
            return false;
        }

        documents[uri] = analysis;
        revisions[uri] = revisions.GetValueOrDefault(uri) + 1;
        if (version is { } documentVersion)
        {
            versions[uri] = documentVersion;
        }

        return true;
    }

    internal bool ReplaceIfCurrent(
        string uri,
        AnalysisResult analysis,
        int expectedRevision
    )
    {
        if (!documents.ContainsKey(uri)
            || revisions.GetValueOrDefault(uri) != expectedRevision)
        {
            return false;
        }

        documents[uri] = analysis;
        revisions[uri] = expectedRevision + 1;

        return true;
    }

    internal bool IsStale(string uri, int? version, bool allowSameVersion)
    {
        if (version is not { } candidate)
        {
            return false;
        }

        if (!versions.TryGetValue(uri, out var current))
        {
            return false;
        }

        return allowSameVersion ? candidate < current : candidate <= current;
    }
}

internal sealed record OpenDocumentSnapshot(
    string Uri,
    AnalysisResult Analysis,
    int Revision
);
