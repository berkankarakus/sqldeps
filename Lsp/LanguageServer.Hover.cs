using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

/// <summary>Provides object and diagnostic explanations for LSP hover requests.</summary>
public sealed partial class LanguageServer
{
    private object? Hover(JsonElement parameters)
    {
        if (TryDiagnosticHover(parameters, out var diagnosticHover))
        {
            return diagnosticHover;
        }

        var name = NameAt(parameters);
        if (name is null)
        {
            return null;
        }

        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, name));
        if (match.Object is null)
        {
            return null;
        }

        var parametersForObject = AllParameters()
            .Where(parameter => SameName(parameter.ObjectName, match.Object.Name))
            .OrderBy(parameter => parameter.Ordinal);
        var signature = string.Join(", ", parametersForObject.Select(FormatParameter));
        var value = $"**{match.Object.Kind}** `{match.Object.Name}`\n\nDefined in `{match.Object.SourceFile}:{match.Object.Line}`";
        value += RepositoryContextHover(match.Object.Name);

        if (signature.Length > 0)
        {
            value += "\n\n`(" + signature + ")`";
        }

        return MarkdownHover(value);
    }

    private bool TryDiagnosticHover(JsonElement parameters, out object? hover)
    {
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            hover = null;

            return false;
        }

        var findings = document.LintFindings
            .Where(item => IsDiagnosticPosition(item, line, character))
            .OrderBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Length)
            .ToArray();

        if (findings.Length == 0)
        {
            hover = null;

            return false;
        }

        hover = MarkdownHover(string.Join("\n\n---\n\n", findings.Select(FormatDiagnosticHover)));
        return true;
    }

    private static bool IsDiagnosticPosition(LintFinding finding, int line, int character)
    {
        var diagnosticLine = finding.Line - 1;
        var diagnosticColumn = Math.Max(0, finding.Column - 1);
        var diagnosticEnd = diagnosticColumn + Math.Max(1, finding.Length);

        return diagnosticLine == line && character >= diagnosticColumn && character < diagnosticEnd;
    }

    private string FormatDiagnosticHover(LintFinding finding)
    {
        var metadata = DiagnosticMetadataFor("live-document", finding.Rule, finding.Severity);
        var value = $"**sqldeps: {finding.Rule}**\n\n"
            + $"{finding.Message}\n\n"
            + $"- Severity: `{finding.Severity}`\n"
            + $"- Confidence: `{metadata.Confidence}`\n"
            + $"- Source: `{metadata.Source}`";

        if (metadata.ConfidenceReason is { } reason)
        {
            value += $"\n- Confidence reason: `{reason}`";
        }

        if (DiagnosticRuleCatalog.TryGet(finding.Rule, out var explanation))
        {
            value += $"\n\n**Why:** {explanation.Summary}\n\n**Suggested direction:** {explanation.Guidance}";
            value += FormatReferences(explanation.References);
        }

        return value + $"\n- Object: `{finding.SourceObject}`\n- Location: `{finding.SourceFile}:{finding.Line}`";
    }

    private static string FormatReferences(IReadOnlyList<DiagnosticReference>? references)
    {
        if (references is null || references.Count == 0)
        {
            return "";
        }

        return "\n\n**Evidence:**\n"
            + string.Join(
                "\n",
                references.Select(reference => $"- [{reference.Title}]({reference.Url}): {reference.Note}"));
    }

    private static string FormatParameter(ObjectParameter parameter)
    {
        var output = parameter.IsOutput ? " OUTPUT" : "";

        return parameter.Name + " " + parameter.DataType + output;
    }

    private static object MarkdownHover(string value) => new
    {
        contents = new
        {
            kind = "markdown",
            value
        },
    };
}
