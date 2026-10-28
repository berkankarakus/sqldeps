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

    private readonly List<IndexedFile> _files = [];

    public int FileCount => _files.Count;

    public int ReusedFileCount { get; private set; }

    public IReadOnlyList<IndexedFile> Files => _files;

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => _parameters;

    public IReadOnlyList<ColumnUsage> ColumnUsage => _columnUsage;

    public IReadOnlyList<ParseProblem> Problems => _problems;

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
            _parameters.AddRange(cached.Parameters);
            _columnUsage.AddRange(cached.ColumnUsage);
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
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;

        using (StringReader reader = new(batch.Text))
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
}
