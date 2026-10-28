using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache
)
{
    private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules"
    };

    private readonly List<DatabaseObject> _objects = new List<DatabaseObject>();
    private readonly List<Dependency> _dependencies = new List<Dependency>();
    private readonly List<ParseProblem> _problems = new List<ParseProblem>();

    private readonly List<IndexedFile> _files = new List<IndexedFile>();

    public int FileCount => _files.Count;

    public int ReusedFileCount { get; private set; }

    public IReadOnlyList<IndexedFile> Files => _files;

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ParseProblem> Problems => _problems;

    public void Index(string rootFolder)
    {
        foreach (var file in FindSqlFiles(rootFolder))
        {
            var relativePath = Path.GetRelativePath(rootFolder, file);
            IndexFile(file, relativePath);
        }
    }

    private static IEnumerable<string> FindSqlFiles(string rootFolder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(rootFolder, "*.sql", options))
        {
            var relativePath = Path.GetRelativePath(rootFolder, file);
            var folders = relativePath.Split(Path.DirectorySeparatorChar).SkipLast(1);

            if (folders.Any(folder => SkippedFolders.Contains(folder)))
            {
                continue;
            }

            yield return file;
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
            _problems.AddRange(cached.Problems);
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
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;

        using (var reader = new StringReader(batch.Text))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _problems.Add(new ParseProblem(relativePath, batch.StartLine + error.Line - 1, error.Message));
            }

            return;
        }

        var script = fragment as TSqlScript;

        if (script is null)
        {
            return;
        }

        var cteNames = CteNameCollector.Collect(script);
        var collector = new DependencyCollector(relativePath, batch.StartLine, cteNames, defaultSchema);
        collector.Collect(script);

        _objects.AddRange(collector.Objects);
        _dependencies.AddRange(collector.Dependencies);
    }
}
