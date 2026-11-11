using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class QueryColumnValidator
{
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
