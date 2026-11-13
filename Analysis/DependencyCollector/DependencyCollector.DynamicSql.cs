using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Parses and records dependencies found inside dynamic SQL.</summary>
internal sealed partial class DependencyCollector
{
    private void CollectDynamicSql(IEnumerable<ValueExpression>? expressions, TSqlFragment at)
    {
        if (expressions is null)
        {
            RecordUnparsedDynamicSql(at, isConcatenated: false);

            return;
        }

        if (DynamicSqlText.TryReadConstant(expressions, out var sql, out var firstLiteral))
        {
            CollectDynamicSql(sql, firstLiteral, at);

            return;
        }

        if (TryReadKnownDynamicSql(expressions, out sql))
        {
            CollectDynamicSql(sql, LineOf(at), at);

            return;
        }

        RecordDynamicSqlCandidates(
            DynamicSqlText.LiteralFragments(expressions),
            at,
            DynamicSqlText.UnresolvedVariableNames(expressions));
        RecordUnparsedDynamicSql(at, DynamicSqlText.HasConcatenation(expressions));
    }

    private void CollectDynamicSql(ScalarExpression? expression, TSqlFragment at)
    {
        if (DynamicSqlText.TryReadConstant(expression, out var sql, out var firstLiteral))
        {
            CollectDynamicSql(sql, LineOf(firstLiteral), at);

            return;
        }

        if (TryReadKnownDynamicSql(expression, out sql))
        {
            CollectDynamicSql(sql, LineOf(at), at);

            return;
        }

        RecordDynamicSqlCandidates(
            DynamicSqlText.LiteralFragments(expression),
            at,
            DynamicSqlText.UnresolvedVariableNames(expression));
        RecordUnparsedDynamicSql(at, DynamicSqlText.HasConcatenation(expression));
    }

    private void CollectDynamicSql(string sql, StringLiteral firstLiteral, TSqlFragment at)
    {
        CollectDynamicSql(sql, LineOf(firstLiteral), at);
    }

    private void CollectDynamicSql(string sql, int sqlLine, TSqlFragment at)
    {
        using StringReader reader = new(sql);
        var fragment = _parser.Parse(reader, out IList<ParseError> errors);

        if (errors.Count > 0)
        {
            RecordDynamicSqlCandidates([sql], at);
            AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

            return;
        }

        if (fragment is not TSqlScript script)
        {
            return;
        }

        var nested = new DependencyCollector(
            sourceFile,
            sqlLine,
            CteNameCollector.Collect(script),
            defaultSchema,
            sql,
            defaultDatabase);
        nested.CollectInside(script, _scope);
        _dependencies.AddRange(nested.Dependencies.Select(dependency => dependency with { ViaDynamic = true }));
        _columnUsage.AddRange(nested.ColumnUsage);
        _tableColumns.AddRange(nested.TableColumns);
        _problems.AddRange(nested.Problems);
        _lintFindings.AddRange(nested.LintFindings);
        _dynamicSqlCandidates.AddRange(nested.DynamicSqlCandidates);
    }

    private void RecordDynamicSqlCandidates(
        IEnumerable<string> literalFragments,
        TSqlFragment at,
        string? provenance = null)
    {
        _dynamicSqlCandidates.AddRange(
            DynamicSqlCandidateExtractor.Extract(
                literalFragments,
                _scope,
                sourceFile,
                LineOf(at),
                name => ObjectNames.Qualify(name, defaultSchema, defaultDatabase),
                provenance));
    }

    private bool TryReadKnownDynamicSql(ScalarExpression? expression, out string sql)
    {
        sql = "";

        if (_constantVariables.Count == 0)
        {
            return false;
        }

        ConstantVariableLiteralResolver resolver = new(
            _constantVariables,
            _temporalVariables);

        if (!resolver.TryLiteral(expression, out var value)
            || value is not StringLiteral literal)
        {
            return false;
        }

        sql = literal.Value;

        return true;
    }

    private bool TryReadKnownDynamicSql(IEnumerable<ValueExpression> expressions, out string sql)
    {
        sql = "";
        System.Text.StringBuilder text = new();
        var foundValue = false;

        foreach (var expression in expressions)
        {
            if (expression is not ScalarExpression scalar
                || !TryReadKnownDynamicSql(scalar, out var value))
            {
                return false;
            }

            text.Append(value);
            foundValue = true;
        }

        if (!foundValue)
        {
            return false;
        }

        sql = text.ToString();

        return true;
    }

    private void RecordUnparsedDynamicSql(TSqlFragment at, bool isConcatenated)
    {
        AddDependency(ObjectNames.DynamicSql, "EXEC_DYNAMIC", at);

        if (isConcatenated)
        {
            AddLint("dynamic-sql-concatenation", "warning", at, "Dynamic SQL is built by string concatenation; use parameters with sp_executesql for data values.");
        }
    }

    private void ReportUnresolvedDirectDynamicSql(
        ScalarExpression? expression,
        TSqlFragment at)
    {
        if (expression is null)
        {
            return;
        }

        if (DynamicSqlText.HasConcatenation(expression)
            || TryReadKnownDynamicSql(expression, out _)
            || DynamicSqlText.UnresolvedVariableNames(expression) is null)
        {
            return;
        }

        AddUnresolvedDirectDynamicSqlFinding(at);
    }

    private void ReportUnresolvedDirectDynamicSql(
        IEnumerable<ValueExpression>? expressions,
        TSqlFragment at)
    {
        if (expressions is null
            || DynamicSqlText.HasConcatenation(expressions)
            || TryReadKnownDynamicSql(expressions, out _)
            || DynamicSqlText.UnresolvedVariableNames(expressions) is null)
        {
            return;
        }

        AddUnresolvedDirectDynamicSqlFinding(at);
    }

    private void AddUnresolvedDirectDynamicSqlFinding(TSqlFragment at)
    {
        AddLint(
            "dynamic-sql-unresolved",
            "warning",
            at,
            "Dynamic SQL passed directly to EXEC could not be resolved; validate its source and use sp_executesql parameters for data values.");
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
