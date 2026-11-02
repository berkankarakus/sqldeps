using System.Text.Json;
using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object SemanticTokens(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var analysis = OpenDocument(uri);
        if (analysis is null)
        {
            return new { data = Array.Empty<int>() };
        }

        var objects = AllObjects()
            .Select(item => item.Object)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tokens = new HashSet<SemanticToken>(SemanticTokenComparer.Instance);
        var maskedText = MaskNonCode(analysis.Text);
        var lines = maskedText.Split('\n');

        foreach (var definition in analysis.Objects)
        {
            AddObjectTokens(lines, definition.Line - 1, [definition.Name], objects, tokens);
        }

        foreach (var dependency in analysis.Dependencies.Where(item => SamePath(item.SourceFile, analysis.SourceFile)))
        {
            AddObjectTokens(lines, dependency.Line - 1, [dependency.TargetObject], objects, tokens);
        }

        var ordered = tokens
            .OrderBy(token => token.Line)
            .ThenBy(token => token.Start)
            .ToArray();
        var encoded = new List<int>(ordered.Length * 5);
        var previousLine = 0;
        var previousStart = 0;
        foreach (var token in ordered)
        {
            var deltaLine = token.Line - previousLine;
            encoded.Add(deltaLine);
            encoded.Add(deltaLine == 0 ? token.Start - previousStart : token.Start);
            encoded.Add(token.Length);
            encoded.Add(token.Type);
            encoded.Add(0);
            previousLine = token.Line;
            previousStart = token.Start;
        }

        return new { data = encoded.ToArray() };
    }

    private static void AddObjectTokens(
        IReadOnlyList<string> lines,
        int lineNumber,
        IReadOnlyList<string> expectedNames,
        IReadOnlyList<IGrouping<string, DatabaseObject>> objects,
        ISet<SemanticToken> tokens)
    {
        if (lineNumber < 0 || lineNumber >= lines.Count)
        {
            return;
        }

        var line = lines[lineNumber];
        foreach (Match match in ObjectTokenPattern().Matches(line))
        {
            var writtenName = NormalizeObjectName(match.Value);
            var resolved = objects
                .Where(group => expectedNames.Any(expected => SameName(group.Key, expected)))
                .SelectMany(group => group)
                .Where(item => SameObjectToken(item.Name, writtenName))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (resolved.Length != 1)
            {
                continue;
            }

            var kind = resolved[0].First().Kind;
            tokens.Add(new SemanticToken(lineNumber, match.Index, match.Length, kind is "procedure" or "function" or "trigger" ? 1 : 0));
        }
    }

    private static bool SameObjectToken(string objectName, string writtenName)
    {
        if (SameName(objectName, writtenName))
        {
            return true;
        }

        return !writtenName.Contains('.')
            && objectName.EndsWith("." + writtenName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeObjectName(string value)
    {
        return string.Join('.', value.Split('.').Select(part => part.Trim('[', ']')));
    }

    private static string MaskNonCode(string text)
    {
        var characters = text.ToCharArray();
        var inString = false;
        var inBlockComment = false;
        for (var index = 0; index < characters.Length; index++)
        {
            if (inBlockComment)
            {
                if (characters[index] == '*' && index + 1 < characters.Length && characters[index + 1] == '/')
                {
                    characters[index++] = ' ';
                    characters[index] = ' ';
                    inBlockComment = false;
                }
                else if (characters[index] is not '\n' and not '\r')
                {
                    characters[index] = ' ';
                }

                continue;
            }

            if (inString)
            {
                if (characters[index] == '\'' && index + 1 < characters.Length && characters[index + 1] == '\'')
                {
                    characters[index++] = ' ';
                    characters[index] = ' ';
                }
                else if (characters[index] == '\'')
                {
                    characters[index] = ' ';
                    inString = false;
                }
                else if (characters[index] is not '\n' and not '\r')
                {
                    characters[index] = ' ';
                }

                continue;
            }

            if (characters[index] == '-' && index + 1 < characters.Length && characters[index + 1] == '-')
            {
                characters[index++] = ' ';
                while (index < characters.Length && characters[index] is not '\n' and not '\r')
                {
                    characters[index++] = ' ';
                }

                index--;
                continue;
            }

            if (characters[index] == '/' && index + 1 < characters.Length && characters[index + 1] == '*')
            {
                characters[index++] = ' ';
                characters[index] = ' ';
                inBlockComment = true;
                continue;
            }

            if (characters[index] == '\'')
            {
                characters[index] = ' ';
                inString = true;
            }
        }

        return new string(characters);
    }

    [GeneratedRegex(@"(?<![\w$#@])(?:\[[^\]\r\n]+\]|[A-Za-z_][\w$#@]*)(?:\.(?:\[[^\]\r\n]+\]|[A-Za-z_][\w$#@]*)){0,3}(?![\w$#@])")]
    private static partial Regex ObjectTokenPattern();

    private readonly record struct SemanticToken(int Line, int Start, int Length, int Type);

    private sealed class SemanticTokenComparer : IEqualityComparer<SemanticToken>
    {
        public static SemanticTokenComparer Instance { get; } = new();

        public bool Equals(SemanticToken x, SemanticToken y) => x == y;

        public int GetHashCode(SemanticToken obj) => HashCode.Combine(obj.Line, obj.Start, obj.Length, obj.Type);
    }
}
