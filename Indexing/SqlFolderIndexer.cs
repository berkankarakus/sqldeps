using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Indexing;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache
)
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules"
    };

    private readonly List<DatabaseObject> _objects = [];
    private readonly List<Dependency> _dependencies = [];
    private readonly List<ObjectParameter> _parameters = [];
    private readonly List<ColumnUsage> _columnUsage = [];
    private readonly List<ParseProblem> _problems = [];
    private readonly List<IndexWarning> _warnings = [];

    private readonly List<IndexedFile> _files = [];

    public int FileCount => _files.Count;

    public int ReusedFileCount { get; private set; }

    public IReadOnlyList<IndexedFile> Files => _files;

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => _parameters;

    public IReadOnlyList<ColumnUsage> ColumnUsage => _columnUsage;

    public IReadOnlyList<ParseProblem> Problems => _problems;

    public IReadOnlyList<IndexWarning> Warnings => _warnings;

    public void Index(string rootFolder)
    {
        foreach (var file in FindSqlFiles(rootFolder))
        {
            var relativePath = Path.GetRelativePath(rootFolder, file);
            IndexFile(file, relativePath);
        }
    }

    public static IEnumerable<string> FindSqlFiles(string rootFolder)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.sql", options))
        {
            var relativePath = Path.GetRelativePath(rootFolder, file);
            if (IsInSkippedFolder(relativePath))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsInSkippedFolder(string relativePath)
    {
        var folderStart = 0;

        while (true)
        {
            var separator = relativePath.IndexOf(Path.DirectorySeparatorChar, folderStart);

            if (separator < 0)
            {
                return false;
            }

            var folder = relativePath.AsSpan(folderStart, separator - folderStart);

            foreach (var skippedFolder in SkippedFolders)
            {
                if (folder.Equals(skippedFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            folderStart = separator + 1;
        }
    }

    private void IndexFile(
        string path,
        string relativePath
    )
    {
        var bytes = File.ReadAllBytes(path);
        var hash = SqlFileReader.HashOf(bytes);
        _files.Add(new IndexedFile(relativePath, hash));

        if (cache.TryGetValue(relativePath, out var cached) && cached.Hash == hash)
        {
            _objects.AddRange(cached.Objects);
            _dependencies.AddRange(cached.Dependencies);
            _parameters.AddRange(cached.Parameters);
            _columnUsage.AddRange(cached.ColumnUsage);
            _problems.AddRange(cached.Problems);
            _warnings.AddRange(cached.Warnings);
            ReusedFileCount++;

            return;
        }

        foreach (var batch in BatchSplitter.Split(SqlFileReader.Decode(bytes)))
        {
            IndexBatch(batch, relativePath);
        }
    }

    private void IndexBatch(
        SqlBatch batch,
        string relativePath
    )
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;

        using (StringReader reader = new(batch.Text))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0)
        {
            if (IsUserScript(relativePath) && TryIndexValidPrefix(batch, relativePath, errors[0].Line))
            {
                _warnings.Add(new IndexWarning(relativePath, batch.StartLine + errors[0].Line - 1, "Non-SQL text after a valid SQL prefix was skipped."));

                return;
            }

            foreach (var error in errors)
            {
                _problems.Add(new ParseProblem(relativePath, batch.StartLine + error.Line - 1, error.Message));
            }

            return;
        }

        TSqlScript? script = fragment as TSqlScript;

        if (script is null)
        {
            return;
        }

        var cteNames = CteNameCollector.Collect(script);
        DependencyCollector collector = new(relativePath, batch.StartLine, cteNames, defaultSchema);
        collector.Collect(script);

        _objects.AddRange(collector.Objects);
        _dependencies.AddRange(collector.Dependencies);
        _parameters.AddRange(collector.Parameters);
        _columnUsage.AddRange(collector.ColumnUsage);
        _problems.AddRange(collector.Problems);
    }

    private bool TryIndexValidPrefix(SqlBatch batch, string relativePath, int errorLine)
    {
        var prefix = string.Join('\n', batch.Text.Split('\n').Take(errorLine - 1));

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(prefix);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return false;
        }

        DependencyCollector collector = new(relativePath, batch.StartLine, CteNameCollector.Collect(script), defaultSchema);
        collector.Collect(script);
        _objects.AddRange(collector.Objects);
        _dependencies.AddRange(collector.Dependencies);
        _parameters.AddRange(collector.Parameters);
        _columnUsage.AddRange(collector.ColumnUsage);
        _problems.AddRange(collector.Problems);

        return true;
    }

    private static bool IsUserScript(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => string.Equals(part, "UserScripts", StringComparison.OrdinalIgnoreCase));
    }
}
