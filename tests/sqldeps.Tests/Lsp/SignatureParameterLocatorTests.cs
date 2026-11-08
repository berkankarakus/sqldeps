using SqlDeps.Lsp;
using SqlDeps.Domain;

namespace SqlDeps.Tests.Lsp;

public sealed class SignatureParameterLocatorTests
{
    private static readonly ObjectParameter[] Parameters =
    [
        new("dbo.Run", 0, "@id", "int", null, false, "run.sql"),
        new("dbo.Run", 1, "@name", "nvarchar(50)", null, false, "run.sql"),
        new("dbo.Run", 2, "@limit", "int", null, false, "run.sql"),
    ];

    [Fact]
    public void ActiveParameter_ReturnsZeroWhenNoParametersExist()
    {
        var active = SignatureParameterLocator.ActiveParameter("", []);

        Assert.Equal(0, active);
    }

    [Fact]
    public void ActiveParameter_ResolvesNamedArgumentCaseInsensitively()
    {
        var active = SignatureParameterLocator.ActiveParameter("@NAME = NULL", Parameters);

        Assert.Equal(1, active);
    }

    [Fact]
    public void ActiveParameter_IgnoresCommasInsideStringLiterals()
    {
        var active = SignatureParameterLocator.ActiveParameter("N'a,b', 1", Parameters);

        Assert.Equal(1, active);
    }

    [Fact]
    public void ActiveParameter_ClampsBeyondLastParameter()
    {
        var active = SignatureParameterLocator.ActiveParameter("1, 2, 3, 4", Parameters);

        Assert.Equal(2, active);
    }
}
