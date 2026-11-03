using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Separates lint content from the AST location where it was observed.</summary>
internal static class LintDiagnosticFactory
{
    public static LintDiagnosticDefinition Create(string rule, string severity, string message) =>
        new(rule, severity, message);

    public static LintFinding CreateFinding(
        LintDiagnosticDefinition diagnostic,
        string sourceObject,
        string sourceFile,
        int line,
        TSqlFragment node
    ) => new(
        diagnostic.Rule,
        diagnostic.Severity,
        sourceObject,
        sourceFile,
        line,
        node.StartColumn,
        node.FragmentLength,
        diagnostic.Message);
}

internal sealed record LintDiagnosticDefinition(string Rule, string Severity, string Message);
