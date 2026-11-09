namespace SqlDeps.Lsp;

/// <summary>A request failure whose message is safe and useful to show to the user in the client.</summary>
internal sealed class LspRequestFailedException(string message) : Exception(message)
{
}
