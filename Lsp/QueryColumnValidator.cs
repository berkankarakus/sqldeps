using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Globalization;
using System.Text.RegularExpressions;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps.Lsp;

/// <summary>Finds column references that can be checked against the indexed table schema.</summary>
internal static partial class QueryColumnValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        MetadataCatalog metadata,
        IEnumerable<LocalColumn> localColumns
    )
    {
        var validationMetadata = ValidationMetadata.Create(metadata);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            findings.AddRange(ValidateBatch(sourceFile, batch, validationMetadata, localColumns));
        }

        return findings;
    }

    private static IEnumerable<LintFinding> ValidateBatch(
        string sourceFile,
        ParsedSqlBatch batch,
        ValidationMetadata metadata,
        IEnumerable<LocalColumn> localColumns)
    {
        var batchEndLine = batch.StartLine + batch.Text.Count(character => character == '\n');
        var columnsByLocalName = localColumns
            .Where(column => column.Line >= batch.StartLine && column.Line <= batchEndLine)
            .GroupBy(column => column.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var queries = QuerySpecificationFinder.Find(batch.Script).ToArray();
        var cteNames = CteNameCollector.Collect(batch.Script);
        ColumnVisitor visitor = new(
            sourceFile,
            batch.StartLine,
            metadata.ColumnsByTable,
            metadata.MetadataByTable,
            metadata.ResultColumnsByObject,
            metadata.IndexedColumnsByTable,
            metadata.FilteredIndexColumnsByTable,
            metadata.ForeignKeysByColumn,
            columnsByLocalName,
            metadata.TablesByBaseName,
            queries,
            cteNames);
        batch.Script.Accept(visitor);

        return LintSuppressions.Filter(visitor.Findings, batch.Text, batch.StartLine);
    }

    private sealed record ValidationMetadata(
        IReadOnlyDictionary<string, HashSet<string>> ColumnsByTable,
        IReadOnlyDictionary<string, TableColumn[]> MetadataByTable,
        IReadOnlyDictionary<string, ResultColumn[]> ResultColumnsByObject,
        IReadOnlyDictionary<string, HashSet<string>> IndexedColumnsByTable,
        IReadOnlyDictionary<string, TableIndexColumn[]> FilteredIndexColumnsByTable,
        IReadOnlyDictionary<(string TableName, string ColumnName), TableForeignKey> ForeignKeysByColumn,
        IReadOnlyDictionary<string, string[]> TablesByBaseName)
    {
        public static ValidationMetadata Create(MetadataCatalog metadata)
        {
            var indexedColumns = metadata.TableColumns;
            var columnsByTable = indexedColumns
                .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            var metadataByTable = indexedColumns
                .GroupBy(column => column.TableName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var tablesByBaseName = indexedColumns
                .Select(column => column.TableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .GroupBy(name => name.Split('.').Last(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var resultColumnsByObject = metadata.ResultColumns
                .GroupBy(column => column.ObjectName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(column => column.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
            var indexedColumnsByTable = metadata.IndexColumns
                .Where(column => !column.IsIncluded && !column.IsFiltered)
                .GroupBy(column => column.TableName)
                .ToDictionary(group => group.Key, group => group.Select(column => column.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase));
            var filteredIndexColumnsByTable = metadata.IndexColumns
                .Where(column => !column.IsIncluded && column.IsFiltered && column.FilterPredicate is not null)
                .GroupBy(column => column.TableName)
                .ToDictionary(group => group.Key, group => group.ToArray());

            foreach (var key in metadata.TableKeys)
            {
                indexedColumnsByTable.GetValueOrDefault(key.TableName)?.Add(key.ColumnName);
            }

            var foreignKeysByColumn = metadata.ForeignKeys
                .GroupBy(key => (key.TableName, key.ColumnName))
                .ToDictionary(group => group.Key, group => group.First());

            return new(
                columnsByTable,
                metadataByTable,
                resultColumnsByObject,
                indexedColumnsByTable,
                filteredIndexColumnsByTable,
                foreignKeysByColumn,
                tablesByBaseName);
        }
    }

    private sealed partial class ColumnVisitor(
        string sourceFile,
        int batchStartLine,
        IReadOnlyDictionary<string, HashSet<string>> columnsByTable,
        IReadOnlyDictionary<string, TableColumn[]> metadataByTable,
        IReadOnlyDictionary<string, ResultColumn[]> resultColumnsByObject,
        IReadOnlyDictionary<string, HashSet<string>> indexedColumnsByTable,
        IReadOnlyDictionary<string, TableIndexColumn[]> filteredIndexColumnsByTable,
        IReadOnlyDictionary<(string TableName, string ColumnName), TableForeignKey> foreignKeysByColumn,
        IReadOnlyDictionary<string, LocalColumn[]> columnsByLocalName,
        IReadOnlyDictionary<string, string[]> tablesByBaseName,
        IReadOnlyList<QuerySpecification> allQueries,
        IReadOnlySet<string> cteNames
    ) : TSqlFragmentVisitor
    {
        private int _scopeLine;
        private readonly Sql160ScriptGenerator _scriptGenerator = new();

        public List<LintFinding> Findings { get; } = [];

        public override void Visit(ProcedureStatementBody node)
        {
            var previousScope = _scopeLine;
            _scopeLine = LineOf(node);

            try
            {
                base.Visit(node);
            }
            finally
            {
                _scopeLine = previousScope;
            }
        }

        public override void Visit(QuerySpecification node)
        {
            var nestedQueries = NestedQueries(node);
            var tables = TablesFor(node, nestedQueries);
            ReportQueryScopeDiagnostics(node, nestedQueries, tables);
            ReportQueryIndexDiagnostics(node, tables);
            var orderColumns = node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<ColumnReferenceExpression>()
                .ToHashSet() ?? [];

            ValidateQueryColumns(node, nestedQueries, orderColumns, tables);

            ReportQueryOrderDiagnostics(node, orderColumns, tables);
        }

        private int LineOf(TSqlFragment node)
        {
            return batchStartLine + node.StartLine - 1;
        }

        private void Add(string rule, TSqlFragment node, string message, string severity = "error")
        {
            Findings.Add(new LintFinding(
                rule,
                severity,
                "(batch)",
                sourceFile,
                batchStartLine + node.StartLine - 1,
                node.StartColumn,
                node.FragmentLength,
                message));
        }
    }

}
