namespace SqlDeps.Lsp;

/// <summary>Converts between the absolute file URIs used by LSP and local paths.</summary>
internal static class LspUri
{
    public static string PathFromUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            return Path.GetFullPath(parsed.LocalPath);
        }

        throw new ArgumentException("LSP document URI must be an absolute file URI.");
    }

    public static string FromPath(string path)
    {
        var uri = new Uri(Path.GetFullPath(path));
        return uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }
}
