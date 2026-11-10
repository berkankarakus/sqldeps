using SqlDeps.Analysis;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? SuppressLintAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        var indentation = text is null ? "" : LineIndentation(text, diagnostic.Range.Start.Line);
        return QuickFix(
            $"Disable sqldeps '{diagnostic.Rule}' for this line",
            uri,
            new(new(diagnostic.Range.Start.Line, 0), new(diagnostic.Range.Start.Line, 0)),
            indentation + $"-- sqldeps: disable-next-line={diagnostic.Rule}\n",
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected rule is disabled only for the following line; SQL semantics are unchanged.",
            includeRefactorCandidate: false,
            suppressionScope: "line"
        );
    }

    private object? SuppressFileAction(string uri, string? text, LintDiagnostic diagnostic)
    {
        if (text is null)
        {
            return null;
        }

        if (LintSuppressions.HasFileSuppression(text, diagnostic.Rule))
        {
            return null;
        }

        return QuickFix(
            $"Disable sqldeps '{diagnostic.Rule}' for this file",
            uri,
            new(new(0, 0), new(0, 0)),
            $"-- sqldeps: disable-file={diagnostic.Rule}\n",
            text,
            diagnostic.Confidence,
            diagnosticRule: diagnostic.Rule,
            applicability: "The selected rule is disabled for this file; SQL semantics are unchanged.",
            includeRefactorCandidate: false,
            suppressionScope: "file");
    }

    private object? SuppressRepositoryAction(LintDiagnostic diagnostic)
    {
        if (workspaceAnalysisRoot is null)
        {
            return null;
        }

        var configPath = Path.Combine(workspaceAnalysisRoot, ".sqldeps");

        if (Directory.Exists(configPath))
        {
            return null;
        }

        string configText = "";

        if (File.Exists(configPath))
        {
            try
            {
                configText = File.ReadAllText(configPath);
            }

            catch (IOException)
            {
                return null;
            }

            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (LintSuppressions.HasRepositorySuppression(configText, diagnostic.Rule))
        {
            return null;
        }

        var lines = configText.Split('\n');
        var line = lines.Length - 1;
        var character = lines[line].TrimEnd('\r').Length;
        var newText = RepositorySuppressionText(configText, diagnostic.Rule);
        var configUri = LspUri.FromPath(configPath);
        var range = new LspRange(new(line, character), new(line, character));
        var title = $"Disable sqldeps '{diagnostic.Rule}' for this repository";

        return new
        {
            title,
            kind = "quickfix",
            data = ReplacementActionData(
                title,
                configUri,
                diagnostic.Confidence,
                [(range, newText)],
                diagnosticRule: diagnostic.Rule,
                applicability: "The selected rule is disabled in the repository .sqldeps configuration; the file is created when needed and SQL semantics are unchanged.",
                includeRefactorCandidate: false,
                suppressionScope: "repository"),
            edit = new
            {
                changes = new Dictionary<string, object>
                {
                    [configUri] = new[] { new { range, newText } },
                },
            },
        };
    }

    private static string RepositorySuppressionText(string configText, string rule)
    {
        if (configText.Length == 0)
        {
            return $"sqldeps: disable-repo={rule}\n";
        }

        if (configText.EndsWith('\n'))
        {
            return $"-- sqldeps: disable-repo={rule}\n";
        }

        return $"\n-- sqldeps: disable-repo={rule}\n";
    }

    private static string LineIndentation(string text, int lineNumber)
    {
        var lines = text.Split('\n');
        if (lineNumber < 0 || lineNumber >= lines.Length)
        {
            return "";
        }

        var line = lines[lineNumber].TrimEnd('\r');
        var length = 0;
        while (length < line.Length && char.IsWhiteSpace(line[length]))
        {
            length++;
        }

        return line[..length];
    }
}
