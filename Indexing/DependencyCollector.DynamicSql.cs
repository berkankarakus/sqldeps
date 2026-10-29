using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Parses and records dependencies found inside dynamic SQL.</summary>
public sealed partial class DependencyCollector
{
    private void CollectDynamicSql(IEnumerable<ValueExpression>? expressions, TSqlFragment at)
    {
        if (expressions is not null && DynamicSqlText.TryReadConstant(expressions, out var sql, out var firstLiteral))
        {
            CollectDynamicSql(sql, firstLiteral, at);

            return;
        }

        RecordUnparsedDynamicSql(at, DynamicSqlText.HasConcatenation(expressions));
    }

    private void CollectDynamicSql(ScalarExpression? expression, TSqlFragment at)
    {
        if (DynamicSqlText.TryReadConstant(expression, out var sql, out var firstLiteral))
        {
            CollectDynamicSql(sql, firstLiteral, at);

            return;
        }

        RecordUnparsedDynamicSql(at, DynamicSqlText.HasConcatenation(expression));
    }

    private void CollectDynamicSql(string sql, StringLiteral firstLiteral, TSqlFragment at)
    {
        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(sql);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

            return;
        }

        if (fragment is not TSqlScript script)
        {
            return;
        }

        var nested = new DependencyCollector(sourceFile, LineOf(firstLiteral), CteNameCollector.Collect(script), defaultSchema);
        nested.CollectInside(script, _scope);
        _dependencies.AddRange(nested.Dependencies.Select(dependency => dependency with { ViaDynamic = true }));
        _columnUsage.AddRange(nested.ColumnUsage);
        _tableColumns.AddRange(nested.TableColumns);
        _problems.AddRange(nested.Problems);
        _lintFindings.AddRange(nested.LintFindings);
    }

    private void RecordUnparsedDynamicSql(TSqlFragment at, bool isConcatenated)
    {
        AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

        if (isConcatenated)
        {
            AddLint("dynamic-sql-concatenation", "warning", at, "Dynamic SQL is built by string concatenation; use parameters with sp_executesql for data values.");
        }
    }

    private void CollectInside(TSqlScript script, string enclosingScope)
    {
        _scope = enclosingScope;

        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                statement.Accept(this);
            }
        }
    }

    private static ScalarExpression? FirstParameterOf(ExecutableProcedureReference node)
    {
        if (node.Parameters.Count == 0)
        {
            return null;
        }

        return node.Parameters[0].ParameterValue;
    }

    private static bool IsDynamicSqlProcedure(SchemaObjectName name) => string.Equals(
        name.BaseIdentifier.Value,
        "sp_executesql",
        StringComparison.OrdinalIgnoreCase
    );
}
