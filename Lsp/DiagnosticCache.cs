namespace SqlDeps.Lsp;

/// <summary>Caches diagnostics only while both document and workspace revisions remain current.</summary>
internal sealed class DiagnosticCache
{
    private readonly Dictionary<string, CachedDiagnostics> entries = new(StringComparer.Ordinal);

    internal bool TryGet(
        string uri,
        int documentRevision,
        int workspaceRevision,
        string source,
        out object[] diagnostics)
    {
        if (!entries.TryGetValue(uri, out var cached)
            || cached.DocumentRevision != documentRevision
            || cached.WorkspaceRevision != workspaceRevision
            || !string.Equals(cached.Source, source, StringComparison.Ordinal))
        {
            diagnostics = [];

            return false;
        }

        diagnostics = cached.Items.ToArray();

        return true;
    }

    internal void Store(
        string uri,
        int documentRevision,
        int workspaceRevision,
        string source,
        object[] diagnostics)
    {
        entries[uri] = new CachedDiagnostics(
            documentRevision,
            workspaceRevision,
            source,
            diagnostics.ToArray());
    }

    internal void Remove(string uri)
    {
        entries.Remove(uri);
    }

    internal void Clear()
    {
        entries.Clear();
    }

    private sealed record CachedDiagnostics(
        int DocumentRevision,
        int WorkspaceRevision,
        string Source,
        object[] Items
    );
}
