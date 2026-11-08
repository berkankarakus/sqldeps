using System.Text.Json;

namespace SqlDeps.Lsp;

/// <summary>Reads workspace and index settings from the LSP initialize request.</summary>
internal sealed record LspInitializationOptions(
    bool ClientSupportsPullDiagnostics,
    bool ClientSupportsDiagnosticRefresh,
    string? IndexPath,
    bool AllowAutomaticIndexSelection,
    string? WorkspaceRoot,
    bool ExperimentalSuggestions
)
{
    public static LspInitializationOptions Parse(JsonElement parameters)
    {
        return new LspInitializationOptions(
            SupportsPullDiagnostics(parameters),
            SupportsDiagnosticRefresh(parameters),
            InitializationIndexPath(parameters),
            AllowAutomaticSelection(parameters),
            FindWorkspaceRoot(parameters),
            ExperimentalSuggestionsEnabled(parameters)
        );
    }

    private static bool SupportsPullDiagnostics(JsonElement parameters)
    {
        return parameters.TryGetProperty("capabilities", out var capabilities)
            && capabilities.TryGetProperty("textDocument", out var textDocument)
            && textDocument.TryGetProperty("diagnostic", out var diagnostic)
            && diagnostic.ValueKind == JsonValueKind.Object;
    }

    private static bool SupportsDiagnosticRefresh(JsonElement parameters)
    {
        return parameters.TryGetProperty("capabilities", out var capabilities)
            && capabilities.TryGetProperty("workspace", out var workspace)
            && workspace.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.TryGetProperty("refreshSupport", out var refreshSupport)
            && refreshSupport.ValueKind == JsonValueKind.True;
    }

    private static string? FindWorkspaceRoot(JsonElement parameters)
    {
        var rootUri = TryGetPath(parameters, "rootUri");
        if (rootUri is not null)
        {
            return rootUri;
        }

        var rootPath = TryGetPath(parameters, "rootPath");
        if (rootPath is not null)
        {
            return rootPath;
        }

        if (!parameters.TryGetProperty("workspaceFolders", out var folders)
            || folders.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (folders.GetArrayLength() == 0)
        {
            return null;
        }

        return TryGetPath(folders[0], "uri");
    }

    private static string? InitializationIndexPath(JsonElement parameters)
    {
        if (!TryGetInitializationOptions(parameters, out var options))
        {
            return null;
        }

        if (!options.TryGetProperty("indexPath", out var indexPath))
        {
            return null;
        }

        if (indexPath.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(indexPath.GetString()))
        {
            return null;
        }

        return Path.GetFullPath(indexPath.GetString()!);
    }

    private static bool AllowAutomaticSelection(JsonElement parameters)
    {
        if (!TryGetInitializationOptions(parameters, out var options))
        {
            return true;
        }

        if (!options.TryGetProperty("automaticIndexSelection", out var automaticSelection))
        {
            return true;
        }

        if (automaticSelection.ValueKind != JsonValueKind.False)
        {
            return true;
        }

        return false;
    }

    private static bool ExperimentalSuggestionsEnabled(JsonElement parameters)
    {
        if (!TryGetInitializationOptions(parameters, out var options))
        {
            return false;
        }

        if (!options.TryGetProperty("experimentalSuggestions", out var suggestions))
        {
            return false;
        }

        if (suggestions.ValueKind != JsonValueKind.True)
        {
            return false;
        }

        return true;
    }

    private static bool TryGetInitializationOptions(JsonElement parameters, out JsonElement options)
    {
        return parameters.TryGetProperty("initializationOptions", out options)
            && options.ValueKind == JsonValueKind.Object;
    }

    private static string? TryGetPath(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (property.GetString() is not { } text)
        {
            return null;
        }

        return propertyName.EndsWith("Uri", StringComparison.OrdinalIgnoreCase)
            ? LspUri.PathFromUri(text)
            : Path.GetFullPath(text);
    }
}
