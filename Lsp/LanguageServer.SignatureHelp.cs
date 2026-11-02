using System.Text.Json;
using System.Text.RegularExpressions;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object SignatureHelp(JsonElement parameters)
    {
        var (document, line, character) = DocumentAt(parameters);
        if (document is null)
        {
            return EmptySignatureHelp();
        }

        var cursor = OffsetAt(document.Text, line, character);
        var call = FindSignatureCall(document.Text[..cursor]);
        if (call is null)
        {
            return EmptySignatureHelp();
        }

        var objectName = Qualify(call.Value.Name.Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, objectName));
        if (match.Object is null)
        {
            return EmptySignatureHelp();
        }

        var procedureParameters = AllParameters()
            .Where(parameter => SameName(parameter.ObjectName, match.Object.Name))
            .OrderBy(parameter => parameter.Ordinal)
            .ToArray();
        var labels = procedureParameters
            .Select(parameter => parameter.Name + " " + parameter.DataType + (parameter.DefaultValue is null ? "" : " = " + parameter.DefaultValue) + (parameter.IsOutput ? " OUTPUT" : ""))
            .ToArray();
        var signatureLabel = match.Object.Name + "(" + string.Join(", ", labels) + ")";
        var activeParameter = ActiveParameter(call.Value.Arguments, procedureParameters);

        return new
        {
            signatures = new[]
            {
                new
                {
                    label = signatureLabel,
                    documentation = $"{match.Object.Kind} defined in {match.Object.SourceFile}:{match.Object.Line}",
                    parameters = labels.Select(label => new { label }).ToArray(),
                },
            },
            activeSignature = 0,
            activeParameter,
        };
    }

    private static object EmptySignatureHelp() => new
    {
        signatures = Array.Empty<object>(),
        activeSignature = 0,
        activeParameter = 0,
    };

    private static SignatureCall? FindSignatureCall(string text)
    {
        var candidates = new[]
        {
            FunctionSignaturePattern().Match(text),
            ExecuteSignaturePattern().Match(text),
        };

        var match = candidates
            .Where(candidate => candidate.Success)
            .OrderBy(candidate => candidate.Index)
            .LastOrDefault();

        return match is null || !match.Success
            ? null
            : new SignatureCall(match.Groups["name"].Value, match.Groups["args"].Value);
    }

    private static int ActiveParameter(string arguments, IReadOnlyList<ObjectParameter> parameters)
    {
        if (parameters.Count == 0)
        {
            return 0;
        }

        var lastArgument = arguments[(arguments.LastIndexOf(',') + 1)..].Trim();
        var named = NamedArgumentPattern().Match(lastArgument);
        if (named.Success)
        {
            var namedIndex = Array.FindIndex(parameters.ToArray(), parameter => string.Equals(parameter.Name, named.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
            if (namedIndex >= 0)
            {
                return namedIndex;
            }
        }

        var commas = 0;
        var quoted = false;
        for (var index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] == '\'')
            {
                if (quoted && index + 1 < arguments.Length && arguments[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                quoted = !quoted;
            }
            else if (!quoted && arguments[index] == ',')
            {
                commas++;
            }
        }

        return Math.Min(commas, parameters.Count - 1);
    }

    private readonly record struct SignatureCall(string Name, string Arguments);

    [GeneratedRegex(@"(?<name>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})\s*\((?<args>[^()]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionSignaturePattern();

    [GeneratedRegex(@"\bEXEC(?:UTE)?\s+(?<name>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?<args>\s+[^;]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExecuteSignaturePattern();

    [GeneratedRegex(@"^(?<name>@[\w]+)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex NamedArgumentPattern();
}
