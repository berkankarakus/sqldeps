using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Extracts only explicitly named objects from literal dynamic SQL fragments.</summary>
internal static partial class DynamicSqlCandidateExtractor
{
    internal static IEnumerable<DynamicSqlCandidate> Extract(
        IEnumerable<string> literalFragments,
        string sourceObject,
        string sourceFile,
        int line,
        Func<string, string> qualify,
        string? provenance = null)
    {
        HashSet<(string Target, string Operation)> seen = [];
        string confidence = provenance is null ? "literal" : "unresolved";
        string evidence = provenance is null
            ? "Explicit object name found in a literal dynamic SQL fragment."
            : "Explicit object name found in a literal dynamic SQL fragment with unresolved variable(s): " + provenance + ".";

        foreach (var fragment in literalFragments)
        {
            string masked = MaskNestedStringLiterals(fragment);

            foreach (Match match in ObjectReferencePattern().Matches(masked))
            {
                if (!TryNormalizeIdentifier(match.Groups["name"].Value, out var target))
                {
                    continue;
                }

                string operation = OperationOf(match.Groups["operation"].Value);
                string qualifiedTarget = qualify(target);

                if (seen.Add((qualifiedTarget, operation)))
                {
                    yield return new DynamicSqlCandidate(
                        sourceObject,
                        qualifiedTarget,
                        operation,
                        confidence,
                        evidence,
                        sourceFile,
                        line);
                }
            }
        }
    }

    private static bool TryNormalizeIdentifier(string value, out string normalized)
    {
        string[] parts = value.Split('.', StringSplitOptions.TrimEntries);
        List<string> names = [];

        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                normalized = "";

                return false;
            }

            if (part.StartsWith('[') && part.EndsWith(']'))
            {
                var unquoted = part[1..^1].Replace("]]", "]", StringComparison.Ordinal);

                if (unquoted.Contains('.', StringComparison.Ordinal))
                {
                    normalized = "";

                    return false;
                }

                names.Add(unquoted);
            }
            else
            {
                names.Add(part);
            }
        }

        normalized = string.Join('.', names);

        return normalized.Length > 0;
    }

    private static string OperationOf(string keyword)
    {
        return keyword.Trim().ToUpperInvariant() switch
        {
            "EXEC" or "EXECUTE" => "EXEC",
            "INSERT INTO" => "INSERT",
            "DELETE FROM" => "DELETE",
            "TRUNCATE TABLE" => "TRUNCATE",
            "UPDATE" => "UPDATE",
            "MERGE" => "MERGE",
            _ => "SELECT",
        };
    }

    private static string MaskNestedStringLiterals(string text)
    {
        char[] masked = text.ToCharArray();
        var insideLiteral = false;

        for (var index = 0; index < masked.Length; index++)
        {
            if (masked[index] != '\'')
            {
                if (insideLiteral)
                {
                    masked[index] = ' ';
                }

                continue;
            }

            if (insideLiteral && index + 1 < masked.Length && masked[index + 1] == '\'')
            {
                masked[index] = ' ';
                masked[index + 1] = ' ';
                index++;

                continue;
            }

            masked[index] = ' ';
            insideLiteral = !insideLiteral;
        }

        return MaskComments(new string(masked));
    }

    private static string MaskComments(string text)
    {
        char[] masked = text.ToCharArray();
        var lineComment = false;
        var blockComment = false;

        for (var index = 0; index < masked.Length; index++)
        {
            if (lineComment)
            {
                if (masked[index] is '\r' or '\n')
                {
                    lineComment = false;
                }
                else
                {
                    masked[index] = ' ';
                }

                continue;
            }

            if (blockComment)
            {
                if (index + 1 < masked.Length && masked[index] == '*' && masked[index + 1] == '/')
                {
                    masked[index] = ' ';
                    masked[index + 1] = ' ';
                    index++;
                    blockComment = false;
                }
                else
                {
                    masked[index] = ' ';
                }

                continue;
            }

            if (index + 1 < masked.Length && masked[index] == '-' && masked[index + 1] == '-')
            {
                masked[index] = ' ';
                masked[index + 1] = ' ';
                index++;
                lineComment = true;
            }
            else if (index + 1 < masked.Length && masked[index] == '/' && masked[index + 1] == '*')
            {
                masked[index] = ' ';
                masked[index + 1] = ' ';
                index++;
                blockComment = true;
            }
        }

        return new string(masked);
    }

    [GeneratedRegex(@"(?ix)\b(?<operation>EXEC(?:UTE)?|INSERT\s+INTO|DELETE\s+FROM|TRUNCATE\s+TABLE|UPDATE|MERGE|USING|JOIN|FROM)\s+(?<name>(?:\[[^\]]+\]|[A-Za-z_#][A-Za-z0-9_$#]*)(?:\s*\.\s*(?:\[[^\]]+\]|[A-Za-z_#][A-Za-z0-9_$#]*)){0,3})")]
    private static partial Regex ObjectReferencePattern();
}
