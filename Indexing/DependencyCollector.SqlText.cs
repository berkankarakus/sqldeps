using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;

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
}
