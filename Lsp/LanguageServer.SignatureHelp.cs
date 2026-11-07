using System.Text.Json;
using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object SignatureHelp(JsonElement parameters)
    {
        if (!TryGetSignatureData(parameters, out var data))
        {
            return EmptySignatureHelp();
        }

        var signatureLabel = data.Object.Name + "(" + string.Join(", ", data.Labels) + ")";

        return new
        {
            signatures = new[]
            {
                new
                {
                    label = signatureLabel,
                    documentation = $"{data.Object.Kind} defined in {data.Object.SourceFile}:{data.Object.Line}",
                    parameters = data.Labels.Select(label => new { label }).ToArray(),
                },
            },
            activeSignature = 0,
            activeParameter = data.ActiveParameter,
        };
    }

    private bool TryGetSignatureData(JsonElement parameters, out SignatureData data)
    {
        data = null!;
        var (document, line, character) = DocumentAt(parameters);

        if (document is null)
        {
            return false;
        }

        var cursor = OffsetAt(document.Text, line, character);
        var call = FindSignatureCall(document.Text[..cursor]);

        if (call is null)
        {
            return false;
        }

        var objectName = Qualify(call.Value.Name.Replace("[", "").Replace("]", ""));
        var match = AllObjects().FirstOrDefault(item => SameName(item.Object.Name, objectName));

        if (match.Object is null)
        {
            return false;
        }

        var procedureParameters = AllParameters()
            .Where(parameter => SameName(parameter.ObjectName, match.Object.Name))
            .OrderBy(parameter => parameter.Ordinal)
            .ToArray();
        var labels = procedureParameters.Select(SignatureParameterLabel).ToArray();

        data = new(
            match.Object,
            labels,
            SignatureParameterLocator.ActiveParameter(call.Value.Arguments, procedureParameters));

        return true;
    }

    private static string SignatureParameterLabel(ObjectParameter parameter)
    {
        var label = parameter.Name + " " + parameter.DataType;

        if (parameter.DefaultValue is not null)
        {
            label += " = " + parameter.DefaultValue;
        }

        if (parameter.IsOutput)
        {
            label += " OUTPUT";
        }

        return label;
    }

    private static object EmptySignatureHelp() => new
    {
        signatures = Array.Empty<object>(),
        activeSignature = 0,
        activeParameter = 0,
    };

    private sealed record SignatureData(
        DatabaseObject Object,
        IReadOnlyList<string> Labels,
        int ActiveParameter);

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

    private readonly record struct SignatureCall(string Name, string Arguments);

    [GeneratedRegex(@"(?<name>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})\s*\((?<args>[^()]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionSignaturePattern();

    [GeneratedRegex(@"\bEXEC(?:UTE)?\s+(?<name>(?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+)){0,2})(?<args>\s+[^;]*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ExecuteSignaturePattern();

}
