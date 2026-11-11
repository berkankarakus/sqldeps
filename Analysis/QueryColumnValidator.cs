using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Globalization;
using System.Text.RegularExpressions;
using SqlDeps.Domain;
namespace SqlDeps.Analysis;

/// <summary>Finds column references that can be checked against the indexed table schema.</summary>
internal static partial class QueryColumnValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        MetadataCatalog metadata,
        IEnumerable<LocalColumn> localColumns,
        string? fullDocumentText = null,
        string? repositoryRoot = null
    )
    {
        var validationMetadata = QueryColumnValidationMetadata.Create(metadata);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            findings.AddRange(ValidateBatch(sourceFile, batch, validationMetadata, localColumns, fullDocumentText, repositoryRoot));
        }

        return findings;
    }

    private static IEnumerable<LintFinding> ValidateBatch(
        string sourceFile,
        ParsedSqlBatch batch,
        QueryColumnValidationMetadata metadata,
        IEnumerable<LocalColumn> localColumns,
        string? fullDocumentText,
        string? repositoryRoot)
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
            cteNames,
            CommonTableExpressionFinder.Find(batch.Script).ToArray());
        batch.Script.Accept(visitor);

        return LintSuppressions.Filter(
            visitor.Findings,
            batch.Text,
            batch.StartLine,
            fullDocumentText,
            repositoryRoot);
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
        IReadOnlySet<string> cteNames,
        IReadOnlyList<CommonTableExpression> commonTableExpressions
    ) : TSqlFragmentVisitor
    {
        private int _scopeLine;
        private readonly HashSet<TSqlFragment> _localOutputResolution = [];
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
            var hasUnresolvedDirectTables = HasUnresolvedDirectTables(node, nestedQueries);
            ReportQueryScopeDiagnostics(node, nestedQueries, tables);
            ReportQueryIndexDiagnostics(node, tables);
            var orderColumns = node.OrderByClause?.OrderByElements
                .Select(element => element.Expression)
                .OfType<ColumnReferenceExpression>()
                .ToHashSet() ?? [];

            ValidateQueryColumns(node, nestedQueries, orderColumns, tables, hasUnresolvedDirectTables);

            ReportQueryOrderDiagnostics(node, orderColumns, tables, hasUnresolvedDirectTables);
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
