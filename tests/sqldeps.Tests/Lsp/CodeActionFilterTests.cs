using System.Text.Json;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class CodeActionFilterTests
{
    [Fact]
    public void Filter_WithoutRequestedKinds_ReturnsAllActions()
    {
        object[] actions =
        [
            new { title = "Fix", kind = "quickfix" },
            new { title = "Refactor", kind = "refactor" },
        ];

        using JsonDocument parameters = JsonDocument.Parse("{}");

        var result = CodeActionFilter.Filter(actions, parameters.RootElement);

        Assert.Equal(actions, result);
    }

    [Fact]
    public void Filter_RequestedParentKindIncludesNestedActions()
    {
        object[] actions =
        [
            new { title = "Fix", kind = "quickfix" },
            new { title = "Fix import", kind = "quickfix.import" },
            new { title = "Refactor", kind = "refactor" },
        ];
        using JsonDocument parameters = JsonDocument.Parse(
            "{\"context\":{\"only\":[\"quickfix\"]}}"
        );

        var result = CodeActionFilter.Filter(actions, parameters.RootElement);

        Assert.Equal(2, result.Length);
        Assert.Contains(result, action => action.ToString()!.Contains("Fix", StringComparison.Ordinal));
        Assert.Contains(result, action => action.ToString()!.Contains("Fix import", StringComparison.Ordinal));
    }

    [Fact]
    public void Filter_IgnoresActionsWithoutAStringKind()
    {
        object[] actions =
        [
            new { title = "Missing kind" },
            new { title = "Wrong kind", kind = 4 },
            new { title = "Fix", kind = "quickfix" },
        ];
        using JsonDocument parameters = JsonDocument.Parse(
            "{\"context\":{\"only\":[\"quickfix\"]}}"
        );

        var result = CodeActionFilter.Filter(actions, parameters.RootElement);

        var action = Assert.Single(result);
        Assert.Contains("Fix", action.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_UsesExactKindBoundaries()
    {
        object[] actions =
        [
            new { title = "Exact", kind = "quickfix" },
            new { title = "Nested", kind = "quickfix.generate" },
            new { title = "Near match", kind = "quickfixer" },
        ];
        using JsonDocument parameters = JsonDocument.Parse(
            "{\"context\":{\"only\":[\"quickfix\"]}}"
        );

        var result = CodeActionFilter.Filter(actions, parameters.RootElement);

        Assert.Equal(2, result.Length);
        Assert.DoesNotContain(result, action => action.ToString()!.Contains("Near match", StringComparison.Ordinal));
    }
}
