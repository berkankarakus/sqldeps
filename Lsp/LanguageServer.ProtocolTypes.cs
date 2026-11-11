using System.Text.Json.Serialization;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private const int JsonRpcInvalidRequestCode = -32600;
    private const int JsonRpcInvalidParametersCode = -32602;
    private const int JsonRpcRequestFailedCode = -32803;
    private const int UnixBrokenPipeErrorCode = 32;
    private const int LspCompletionItemKindField = 5;
    private const int LspSymbolKindClass = 5;
    private const int LspSymbolKindFunction = 12;
    private const int LspSymbolKindObject = 19;
    private const int LspSymbolKindStruct = 23;
    private const int LspDiagnosticSeverityError = 1;
    private const int LspDiagnosticSeverityWarning = 2;
    private const int LspDiagnosticSeverityInformation = 3;

    private static object Symbol(DatabaseObject item) => new
    {
        name = item.Name,
        detail = item.Kind,
        kind = SymbolKind(item.Kind),
        range = Range(item.Line),
        selectionRange = Range(item.Line),
    };

    private static int SymbolKind(string kind) => kind switch
    {
        "table" => LspSymbolKindStruct,
        "view" => LspSymbolKindClass,
        "procedure" or "function" or "trigger" => LspSymbolKindFunction,
        _ => LspSymbolKindObject,
    };

    private static int DiagnosticSeverity(string severity) => severity switch
    {
        "error" => LspDiagnosticSeverityError,
        "warning" => LspDiagnosticSeverityWarning,
        _ => LspDiagnosticSeverityInformation,
    };

    private static int DiagnosticSeverity(string severity, bool stale) => stale
        ? LspDiagnosticSeverityInformation
        : DiagnosticSeverity(severity);

    private static DiagnosticMetadata DiagnosticMetadataFor(
        string source,
        string? rule = null,
        string? severity = null)
        => DiagnosticMetadataMapper.For(source, rule, severity);

    private static object DiagnosticData(DiagnosticMetadata metadata, string? rule = null) => new
    {
        source = metadata.Source,
        confidence = metadata.Confidence,
        stale = metadata.IsStale,
        confidenceReason = metadata.ConfidenceReason,
        evidence = DiagnosticEvidence(rule),
    };

    private static object? DiagnosticEvidence(string? rule)
    {
        if (rule is null || !DiagnosticRuleCatalog.TryGet(rule, out var explanation))
        {
            return null;
        }

        return new
        {
            rule = explanation.Rule,
            summary = explanation.Summary,
            guidance = explanation.Guidance,
            references = explanation.References ?? [],
        };
    }

    private static LspLocation Location(string path, int line) => new(LspUri.FromPath(path), Range(line));

    private static LspRange Range(int oneBasedLine) => new(
        new(Math.Max(0, oneBasedLine - 1), 0),
        new(Math.Max(0, oneBasedLine - 1), 1));

    private static LspRange Range(int oneBasedLine, int oneBasedColumn, int length)
    {
        var line = Math.Max(0, oneBasedLine - 1);
        var start = Math.Max(0, oneBasedColumn - 1);

        return new(new(line, start), new(line, start + Math.Max(1, length)));
    }

    private sealed record LspLocation(
        [property: JsonPropertyName("uri")] string Uri,
        [property: JsonPropertyName("range")] LspRange Range
    );

    private sealed record LintDiagnostic(
        string Rule,
        LspRange Range,
        string Message,
        string Source = "",
        string Confidence = "high",
        bool IsStale = false
    );

    private sealed record WorkspaceDiagnosticGroup(string Uri, object[] Diagnostics);
}
