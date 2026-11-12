using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class DiagnosticCacheTests
{
    [Fact]
    public void TryGetRequiresMatchingDocumentWorkspaceAndSourceRevisions()
    {
        DiagnosticCache cache = new();
        object[] diagnostics = [new { message = "warning" }];
        cache.Store("file:///query.sql", 3, 7, "live-document", diagnostics);

        Assert.True(cache.TryGet("file:///query.sql", 3, 7, "live-document", out var actual));
        Assert.NotSame(diagnostics, actual);
        Assert.Same(diagnostics[0], actual[0]);
        actual[0] = new { message = "changed" };
        Assert.True(cache.TryGet("file:///query.sql", 3, 7, "live-document", out var unchanged));
        Assert.Same(diagnostics[0], unchanged[0]);
        Assert.False(cache.TryGet("file:///query.sql", 2, 7, "live-document", out _));
        Assert.False(cache.TryGet("file:///query.sql", 3, 8, "live-document", out _));
        Assert.False(cache.TryGet("file:///query.sql", 3, 7, "pull", out _));
    }

    [Fact]
    public void RemoveInvalidatesOneDocumentWithoutTouchingOtherEntries()
    {
        DiagnosticCache cache = new();
        object[] first = [new { message = "first" }];
        object[] second = [new { message = "second" }];
        cache.Store("file:///first.sql", 1, 1, "live-document", first);
        cache.Store("file:///second.sql", 1, 1, "live-document", second);

        cache.Remove("file:///first.sql");

        Assert.False(cache.TryGet("file:///first.sql", 1, 1, "live-document", out _));
        Assert.True(cache.TryGet("file:///second.sql", 1, 1, "live-document", out var actual));
        Assert.NotSame(second, actual);
        Assert.Same(second[0], actual[0]);
    }
}
