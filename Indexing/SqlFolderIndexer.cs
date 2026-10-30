using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Indexing;

namespace SqlDeps;

/// <summary>Reads every .sql file under a folder and collects what the objects in them depend on.</summary>
public sealed class SqlFolderIndexer(
    string defaultSchema,
    Dictionary<string, CachedFile> cache,
    int? jobs = null
)
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxBatchCharacters = 16 * 1024 * 1024;

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
    private readonly List<ResultColumn> _resultColumns = [];
    private readonly List<ColumnUsage> _columnUsage = [];
    private readonly List<TableColumn> _tableColumns = [];
    private readonly List<TableKey> _tableKeys = [];
    private readonly List<TableForeignKey> _foreignKeys = [];
    private readonly HashSet<string> _tableColumnKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ParseProblem> _problems = [];
    private readonly List<IndexWarning> _warnings = [];
    private readonly List<LintFinding> _lintFindings = [];

    private readonly List<IndexedFile> _files = [];

    public int FileCount => _files.Count;

    public int ReusedFileCount { get; private set; }

    public IReadOnlyList<IndexedFile> Files => _files;

    public IReadOnlyList<DatabaseObject> Objects => _objects;

    public IReadOnlyList<Dependency> Dependencies => _dependencies;

    public IReadOnlyList<ObjectParameter> Parameters => _parameters;

    public IReadOnlyList<ResultColumn> ResultColumns => _resultColumns;

    public IReadOnlyList<ColumnUsage> ColumnUsage => _columnUsage;
    public IReadOnlyList<TableColumn> TableColumns => _tableColumns;

    public IReadOnlyList<TableKey> TableKeys => _tableKeys;

    public IReadOnlyList<TableForeignKey> ForeignKeys => _foreignKeys;

    public IReadOnlyList<ParseProblem> Problems => _problems;

    public IReadOnlyList<IndexWarning> Warnings => _warnings;
    public IReadOnlyList<LintFinding> LintFindings => _lintFindings;

    public void Index(string rootFolder)
    {
        var files = FindSqlFiles(rootFolder)
            .Select(file => (Path: file, RelativePath: Path.GetRelativePath(rootFolder, file)))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        FileIndexResult[] results = new FileIndexResult[files.Length];

        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = jobs ?? -1 }, index =>
        {
            var file = files[index];
            results[index] = IndexFile(file.Path, file.RelativePath);
        });

        foreach (var result in results)
        {
            Merge(result);
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

    private FileIndexResult IndexFile(
        string path,
        string relativePath
    )
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            return OversizedFile(relativePath);
        }

        var bytes = File.ReadAllBytes(path);
        var hash = SqlFileReader.HashOf(bytes);
        FileIndexResult result = new(new IndexedFile(relativePath, hash));

        if (cache.TryGetValue(relativePath, out var cached) && cached.Hash == hash)
        {
            result.Objects.AddRange(cached.Objects);
            result.Dependencies.AddRange(cached.Dependencies);
            result.Parameters.AddRange(cached.Parameters);
            result.ResultColumns.AddRange(cached.ResultColumns);
            result.TableKeys.AddRange(cached.TableKeys);
            result.ForeignKeys.AddRange(cached.ForeignKeys);
            result.ColumnUsage.AddRange(cached.ColumnUsage);
            result.TableColumns.AddRange(cached.TableColumns);
            result.Problems.AddRange(cached.Problems);
            result.Warnings.AddRange(cached.Warnings);
            result.LintFindings.AddRange(cached.LintFindings);
            result.Reused = true;

            return result;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        foreach (var batch in BatchSplitter.Split(SqlFileReader.Decode(bytes)))
        {
            if (batch.Text.Length > MaxBatchCharacters)
            {
                result.Problems.Add(new ParseProblem(relativePath, batch.StartLine, $"SQL batch exceeds the {MaxBatchCharacters / (1024 * 1024)} MiB limit."));

                continue;
            }

            IndexBatch(batch, relativePath, parser, result);
        }

        return result;
    }

    private static FileIndexResult OversizedFile(string relativePath)
    {
        FileIndexResult result = new(new IndexedFile(relativePath, ""));
        result.Problems.Add(new ParseProblem(relativePath, 1, $"SQL file exceeds the {MaxFileBytes / (1024 * 1024)} MiB limit."));

        return result;
    }

    private void IndexBatch(
        SqlBatch batch,
        string relativePath,
        TSql160Parser parser,
        FileIndexResult result
    )
    {
        var script = ParseBatch(batch, parser, out var errors);

        if (errors.Count > 0)
        {
            if (IsUserScript(relativePath) && TryIndexValidPrefix(batch, relativePath, errors[0].Line, result))
            {
                result.Warnings.Add(new IndexWarning(relativePath, batch.StartLine + errors[0].Line - 1, "Non-SQL text after a valid SQL prefix was skipped."));

                return;
            }

            AddParseErrors(batch, relativePath, errors, result);

            return;
        }

        if (script is null)
        {
            return;
        }

        CollectBatch(script, batch, relativePath, result);
    }

    private static TSqlScript? ParseBatch(SqlBatch batch, TSql160Parser parser, out IList<ParseError> errors)
    {
        using StringReader reader = new(batch.Text);
        var fragment = parser.Parse(reader, out errors);

        return fragment as TSqlScript;
    }

    private static void AddParseErrors(
        SqlBatch batch,
        string relativePath,
        IEnumerable<ParseError> errors,
        FileIndexResult result
    )
    {
        foreach (var error in errors)
        {
            result.Problems.Add(new ParseProblem(relativePath, batch.StartLine + error.Line - 1, error.Message));
        }
    }

    private void CollectBatch(
        TSqlScript script,
        SqlBatch batch,
        string relativePath,
        FileIndexResult result
    )
    {
        var cteNames = CteNameCollector.Collect(script);
        DependencyCollector collector = new(relativePath, batch.StartLine, cteNames, defaultSchema, batch.Text);
        collector.Collect(script);

        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.Parameters.AddRange(collector.Parameters);
        result.ResultColumns.AddRange(collector.ResultColumns);
        result.TableKeys.AddRange(collector.TableKeys);
        result.ForeignKeys.AddRange(collector.ForeignKeys);
        result.ColumnUsage.AddRange(collector.ColumnUsage);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(collector.LintFindings, batch.Text, batch.StartLine));
    }

    private bool TryIndexValidPrefix(SqlBatch batch, string relativePath, int errorLine, FileIndexResult result)
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

        DependencyCollector collector = new(relativePath, batch.StartLine, CteNameCollector.Collect(script), defaultSchema, prefix);
        collector.Collect(script);
        result.Objects.AddRange(collector.Objects);
        result.Dependencies.AddRange(collector.Dependencies);
        result.Parameters.AddRange(collector.Parameters);
        result.ResultColumns.AddRange(collector.ResultColumns);
        result.TableKeys.AddRange(collector.TableKeys);
        result.ForeignKeys.AddRange(collector.ForeignKeys);
        result.ColumnUsage.AddRange(collector.ColumnUsage);
        result.TableColumns.AddRange(collector.TableColumns);
        result.Problems.AddRange(collector.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(collector.LintFindings, prefix, batch.StartLine));

        return true;
    }

    private static bool IsUserScript(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => string.Equals(part, "UserScripts", StringComparison.OrdinalIgnoreCase));
    }

    private void Merge(FileIndexResult result)
    {
        _files.Add(result.File);
        _objects.AddRange(result.Objects);
        _dependencies.AddRange(result.Dependencies);
        _parameters.AddRange(result.Parameters);
        _resultColumns.AddRange(result.ResultColumns);
        _tableKeys.AddRange(result.TableKeys);
        _foreignKeys.AddRange(result.ForeignKeys);
        _columnUsage.AddRange(result.ColumnUsage);
        foreach (var column in result.TableColumns)
        {
            if (_tableColumnKeys.Add(column.TableName + "\0" + column.ColumnName))
            {
                _tableColumns.Add(column);
            }
        }
        _problems.AddRange(result.Problems);
        _warnings.AddRange(result.Warnings);
        _lintFindings.AddRange(result.LintFindings);
        ReusedFileCount += result.Reused ? 1 : 0;
    }

    private sealed class FileIndexResult(IndexedFile file)
    {
        public IndexedFile File { get; } = file;
        public bool Reused { get; set; }
        public List<DatabaseObject> Objects { get; } = [];
        public List<Dependency> Dependencies { get; } = [];
        public List<ObjectParameter> Parameters { get; } = [];
        public List<ResultColumn> ResultColumns { get; } = [];
        public List<TableKey> TableKeys { get; } = [];
        public List<TableForeignKey> ForeignKeys { get; } = [];
        public List<ColumnUsage> ColumnUsage { get; } = [];
        public List<TableColumn> TableColumns { get; } = [];
        public List<ParseProblem> Problems { get; } = [];
        public List<IndexWarning> Warnings { get; } = [];
        public List<LintFinding> LintFindings { get; } = [];
    }
}
