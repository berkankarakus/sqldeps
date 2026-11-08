using System.Text.RegularExpressions;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

internal static partial class SignatureParameterLocator
{
    public static int ActiveParameter(
        string arguments,
        IReadOnlyList<ObjectParameter> parameters
    )
    {
        if (parameters.Count == 0)
        {
            return 0;
        }

        var lastArgument = arguments[(arguments.LastIndexOf(',') + 1)..].Trim();
        var named = NamedArgumentPattern().Match(lastArgument);
        if (named.Success)
        {
            var namedIndex = Array.FindIndex(
                parameters.ToArray(),
                parameter => string.Equals(
                    parameter.Name,
                    named.Groups["name"].Value,
                    StringComparison.OrdinalIgnoreCase));
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

    [GeneratedRegex(@"^(?<name>@[\w]+)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex NamedArgumentPattern();
}
