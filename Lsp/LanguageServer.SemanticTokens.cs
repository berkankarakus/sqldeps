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
        var tokens = CollectSemanticTokens(analysis, objects);
        return new { data = SemanticTokenEncoder.Encode(tokens) };
    }

    private static HashSet<SemanticToken> CollectSemanticTokens(
        AnalysisResult analysis,
        IReadOnlyList<IGrouping<string, DatabaseObject>> objects)
    {
        var tokens = new HashSet<SemanticToken>();
        var lines = MaskNonCode(analysis.Text).Split('\n');

        foreach (var definition in analysis.Objects)
        {
            AddObjectTokens(lines, definition.Line - 1, [definition.Name], objects, tokens);
        }

        foreach (var dependency in analysis.Dependencies.Where(item => SamePath(item.SourceFile, analysis.SourceFile)))
        {
            AddObjectTokens(lines, dependency.Line - 1, [dependency.TargetObject], objects, tokens);
        }

        return tokens;
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
        var state = MaskState.Code;

        for (var index = 0; index < characters.Length; index++)
        {
            if (state == MaskState.BlockComment)
            {
                if (MaskBlockCommentCharacter(characters, ref index))
                {
                    state = MaskState.Code;
                }

                continue;
            }

            if (state == MaskState.String)
            {
                if (MaskStringCharacter(characters, ref index))
                {
                    state = MaskState.Code;
                }

                continue;
            }

            if (IsLineCommentStart(characters, index))
            {
                MaskLineComment(characters, ref index);

                continue;
            }

            if (IsBlockCommentStart(characters, index))
            {
                MaskCommentStart(characters, ref index);
                state = MaskState.BlockComment;

                continue;
            }

            if (characters[index] == '\'')
            {
                characters[index] = ' ';
                state = MaskState.String;
            }
        }

        return new string(characters);
    }

    private static bool MaskBlockCommentCharacter(char[] characters, ref int index)
    {
        if (characters[index] == '*' && index + 1 < characters.Length && characters[index + 1] == '/')
        {
            characters[index++] = ' ';
            characters[index] = ' ';

            return true;
        }

        if (characters[index] is not '\n' and not '\r')
        {
            characters[index] = ' ';
        }

        return false;
    }

    private static bool MaskStringCharacter(char[] characters, ref int index)
    {
        if (characters[index] == '\'' && index + 1 < characters.Length && characters[index + 1] == '\'')
        {
            characters[index++] = ' ';
            characters[index] = ' ';

            return false;
        }

        if (characters[index] == '\'')
        {
            characters[index] = ' ';

            return true;
        }

        if (characters[index] is not '\n' and not '\r')
        {
            characters[index] = ' ';
        }

        return false;
    }

    private static void MaskLineComment(char[] characters, ref int index)
    {
        characters[index++] = ' ';

        while (index < characters.Length && characters[index] is not '\n' and not '\r')
        {
            characters[index++] = ' ';
        }

        index--;
    }

    private static void MaskCommentStart(char[] characters, ref int index)
    {
        characters[index++] = ' ';
        characters[index] = ' ';
    }

    private static bool IsLineCommentStart(char[] characters, int index)
    {
        return characters[index] == '-'
            && index + 1 < characters.Length
            && characters[index + 1] == '-';
    }

    private static bool IsBlockCommentStart(char[] characters, int index)
    {
        return characters[index] == '/'
            && index + 1 < characters.Length
            && characters[index + 1] == '*';
    }

    private enum MaskState
    {
        Code,
        String,
        BlockComment,
    }

    [GeneratedRegex(@"(?<![\w$#@])(?:\[[^\]\r\n]+\]|[A-Za-z_][\w$#@]*)(?:\.(?:\[[^\]\r\n]+\]|[A-Za-z_][\w$#@]*)){0,3}(?![\w$#@])")]
    private static partial Regex ObjectTokenPattern();

}
