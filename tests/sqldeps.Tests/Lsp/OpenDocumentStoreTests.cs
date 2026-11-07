using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class OpenDocumentStoreTests
{
    [Fact]
    public void Store_SavesDocumentAndExposesItThroughGetAndValues()
    {
        OpenDocumentStore store = new();
        AnalysisResult analysis = new("document.sql", "SELECT 1;");

        var stored = store.Store("file:///document.sql", analysis, 1, allowSameVersion: false);

        Assert.True(stored);
        Assert.Same(analysis, store.Get("file:///document.sql"));
        Assert.Contains(analysis, store.Values);
        Assert.Equal(1, store.Revision("file:///document.sql"));
    }

    [Fact]
    public void Store_RejectsOlderVersionsAndAllowsSameVersionWhenRequested()
    {
        OpenDocumentStore store = new();
        AnalysisResult first = new("document.sql", "SELECT 1;");
        AnalysisResult replacement = new("document.sql", "SELECT 2;");
        const string uri = "file:///document.sql";

        Assert.True(store.Store(uri, first, 3, allowSameVersion: false));
        Assert.True(store.IsStale(uri, 2, allowSameVersion: false));
        Assert.True(store.IsStale(uri, 3, allowSameVersion: false));
        Assert.False(store.Store(uri, replacement, 2, allowSameVersion: false));
        Assert.True(store.Store(uri, replacement, 3, allowSameVersion: true));
        Assert.Same(replacement, store.Get(uri));
        Assert.Equal(2, store.Revision(uri));
    }

    [Fact]
    public void Remove_ClearsDocumentAndItsVersion()
    {
        OpenDocumentStore store = new();
        const string uri = "file:///document.sql";

        store.Store(uri, new AnalysisResult("document.sql"), 7, allowSameVersion: false);
        store.Remove(uri);

        Assert.Null(store.Get(uri));
        Assert.False(store.IsStale(uri, 1, allowSameVersion: false));
        Assert.Equal(0, store.Revision(uri));
    }
}
