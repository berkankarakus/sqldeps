using System.Text.Json;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private static object[] FilterCodeActions(
        IReadOnlyList<object> actions,
        JsonElement parameters)
    {
        return CodeActionFilter.Filter(actions, parameters);
    }
}
