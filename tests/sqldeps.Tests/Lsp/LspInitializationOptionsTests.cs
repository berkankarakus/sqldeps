using System.Text.Json;
using SqlDeps.Lsp;

namespace SqlDeps.Tests.Lsp;

public sealed class LspInitializationOptionsTests
{
    [Fact]
    public void Parse_UsesRootUriBeforeRootPathAndReadsAllExplicitOptions()
    {
        string indexPath = Path.Combine(Path.GetTempPath(), "sqldeps-index.sqlite");
        string json = JsonSerializer.Serialize(new
        {
            rootUri = "file:///workspace-from-uri",
            rootPath = "/workspace-from-path",
            capabilities = new
            {
                textDocument = new
                {
                    diagnostic = new { },
                },
            },
            initializationOptions = new
            {
                indexPath,
                automaticIndexSelection = false,
                experimentalSuggestions = true,
            },
        });

        using JsonDocument parameters = JsonDocument.Parse(json);

        var options = LspInitializationOptions.Parse(parameters.RootElement);

        Assert.True(options.ClientSupportsPullDiagnostics);
        Assert.Equal(Path.GetFullPath(indexPath), options.IndexPath);
        Assert.False(options.AllowAutomaticIndexSelection);
        Assert.Equal("/workspace-from-uri", options.WorkspaceRoot);
        Assert.True(options.ExperimentalSuggestions);
    }

    [Fact]
    public void Parse_UsesFirstWorkspaceFolderWhenRootIsMissing()
    {
        string workspacePath = Path.Combine(Path.GetTempPath(), "sqldeps-workspace");
        string workspaceUri = LspUri.FromPath(workspacePath);
        using JsonDocument parameters = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                workspaceFolders = new[]
                {
                    new { uri = workspaceUri },
                    new { uri = LspUri.FromPath(Path.Combine(Path.GetTempPath(), "second-workspace")) },
                },
            })
        );

        var options = LspInitializationOptions.Parse(parameters.RootElement);

        Assert.Equal(Path.GetFullPath(workspacePath), options.WorkspaceRoot);
    }

    [Fact]
    public void Parse_UsesSafeDefaultsWhenOptionalPropertiesAreMissingOrInvalid()
    {
        using JsonDocument parameters = JsonDocument.Parse(
            "{\"capabilities\":{\"textDocument\":{\"diagnostic\":null}},\"initializationOptions\":{\"indexPath\":\" \",\"automaticIndexSelection\":\"false\",\"experimentalSuggestions\":1}}"
        );

        var options = LspInitializationOptions.Parse(parameters.RootElement);

        Assert.False(options.ClientSupportsPullDiagnostics);
        Assert.Null(options.IndexPath);
        Assert.True(options.AllowAutomaticIndexSelection);
        Assert.Null(options.WorkspaceRoot);
        Assert.False(options.ExperimentalSuggestions);
    }

    [Fact]
    public void Parse_UsesDefaultSelectionAndNoExperimentalSuggestionsByDefault()
    {
        using JsonDocument parameters = JsonDocument.Parse("{}");

        var options = LspInitializationOptions.Parse(parameters.RootElement);

        Assert.Null(options.IndexPath);
        Assert.True(options.AllowAutomaticIndexSelection);
        Assert.False(options.ExperimentalSuggestions);
        Assert.False(options.ClientSupportsPullDiagnostics);
        Assert.Null(options.WorkspaceRoot);
    }
}
