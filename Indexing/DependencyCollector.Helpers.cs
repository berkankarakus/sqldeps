using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

internal sealed partial class DependencyCollector
{
    private string GenerateSql(TSqlFragment? fragment)
    {
        if (fragment is null)
        {
            return "unknown";
        }

        return GenerateScriptText(fragment).Trim();
    }

    private string GenerateScriptText(TSqlFragment fragment)
    {
        var started = Stopwatch.GetTimestamp();
        _scriptGenerator.GenerateScript(fragment, out var sql);
        _generateScriptTicks += Stopwatch.GetTimestamp() - started;
        _generateScriptCalls++;

        return sql;
    }

    private string GenerateSourceOrSql(TSqlFragment fragment)
    {
        if (TryReadSourceSql(fragment, out var sourceSql))
        {
            _sourceSliceHits++;

            return sourceSql;
        }

        return GenerateSql(fragment);
    }

    private bool TryReadSourceSql(TSqlFragment fragment, out string sql)
    {
        sql = "";

        if (_sourceText is null)
        {
            return false;
        }

        if (fragment.StartOffset < 0)
        {
            return false;
        }

        if (fragment.FragmentLength <= 0)
        {
            return false;
        }

        if (fragment.StartOffset + fragment.FragmentLength > _sourceText.Length)
        {
            return false;
        }

        sql = _sourceText.AsSpan(fragment.StartOffset, fragment.FragmentLength).Trim().ToString();

        return sql.Length > 0;
    }

    private static bool TryIntegerLiteral(ScalarExpression? expression, out long value)
    {
        value = 0;
        return expression switch
        {
            IntegerLiteral literal => long.TryParse(literal.Value, out value),
            ParenthesisExpression { Expression: var inner } => TryIntegerLiteral(inner, out value),
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Positive, Expression: var inner }
                => TryIntegerLiteral(inner, out value),
            UnaryExpression { UnaryExpressionType: UnaryExpressionType.Negative, Expression: var inner }
                => TryNegativeIntegerLiteral(inner, out value),
            _ => false,
        };
    }

    private static bool TryNegativeIntegerLiteral(ScalarExpression expression, out long value)
    {
        value = 0;

        if (!TryIntegerLiteral(expression, out var absolute) || absolute == long.MinValue)
        {
            return false;
        }

        value = -absolute;

        return true;
    }

    private string Format(SchemaObjectName name)
    {
        return ObjectNames.Format(name, defaultSchema);
    }

    private int LineOf(TSqlFragment fragment)
    {
        return batchStartLine + fragment.StartLine - 1;
    }

    private void AddLint(string rule, string severity, TSqlFragment node, string message)
    {
        var diagnostic = LintDiagnosticFactory.Create(rule, severity, message);
        _lintFindings.Add(LintDiagnosticFactory.CreateFinding(diagnostic, _scope, sourceFile, LineOf(node), node));
    }
}
