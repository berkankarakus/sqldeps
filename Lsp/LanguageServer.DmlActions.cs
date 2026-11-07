using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Lsp;

public sealed partial class LanguageServer
{
    private object? PrimaryKeyFilterAction(
        string uri,
        string text,
        LintDiagnostic diagnostic)
    {
        if (!TryGetPrimaryKeyFilter(uri, text, diagnostic, out var insertionRange, out var replacement))
        {
            return null;
        }

        return QuickFix(
            $"Add primary-key filter '{replacement}'",
            uri,
            insertionRange,
            replacement,
            text,
            diagnostic.Confidence,
            ReferencesFor(diagnostic.Rule),
            experimental: true,
            diagnosticRule: diagnostic.Rule,
            applicability: "A single primary key and one matching in-scope SQL parameter must be known.");
    }

    private bool TryGetPrimaryKeyFilter(
        string uri,
        string text,
        LintDiagnostic diagnostic,
        out LspRange insertionRange,
        out string replacement)
    {
        insertionRange = default!;
        replacement = "";

        if (diagnostic.Rule is not ("update-without-where" or "delete-without-where"))
        {
            return false;
        }

        if (!IsDocumentRange(text, diagnostic.Range))
        {
            return false;
        }

        AnalysisResult analysis = AnalyzeDocument(uri, text);
        int targetOffset = OffsetAt(text, diagnostic.Range.Start.Line, diagnostic.Range.Start.Character);

        foreach (var batch in analysis.ParsedBatches)
        {
            int batchOffset = OffsetAt(text, batch.StartLine - 1, 0);
            var visitor = new DmlEvidenceVisitor(diagnostic.Rule);
            batch.Script.Accept(visitor);

            foreach (var evidence in visitor.Statements)
            {
                if (!ContainsOffset(batchOffset, evidence.Statement, targetOffset))
                {
                    continue;
                }

                if (!TryGetPrimaryKeyColumns(evidence.Statement, out var target, out var keyColumns))
                {
                    continue;
                }

                if (!HasMatchingParameters(evidence.Variables, keyColumns))
                {
                    continue;
                }

                string referencePrefix = target.Alias is null
                    ? ""
                    : RenderIdentifier(target.Alias.Value) + ".";
                replacement = " WHERE " + string.Join(
                    " AND ",
                    keyColumns.Select(column =>
                        $"{referencePrefix}{RenderIdentifier(column)} = @{column}"));
                insertionRange = DmlInsertionRange(text, diagnostic.Range.End);

                return IsDocumentRange(text, insertionRange);
            }
        }

        return false;
    }

    private static bool ContainsOffset(
        int batchOffset,
        TSqlStatement statement,
        int targetOffset)
    {
        int statementStart = batchOffset + statement.StartOffset;
        int statementEnd = statementStart + statement.FragmentLength;

        return targetOffset >= statementStart && targetOffset <= statementEnd;
    }

    private bool TryGetPrimaryKeyColumns(
        TSqlStatement statement,
        out NamedTableReference target,
        out string[] keyColumns)
    {
        target = null!;
        keyColumns = [];

        NamedTableReference? resolvedTarget = statement switch
        {
            UpdateStatement update => update.UpdateSpecification.Target as NamedTableReference,
            DeleteStatement delete => delete.DeleteSpecification.Target as NamedTableReference,
            _ => null,
        };

        if (resolvedTarget is null)
        {
            return false;
        }

        target = resolvedTarget;

        if (HasUnsupportedTargetName(target))
        {
            return false;
        }

        string tableName = ObjectNames.Format(target.SchemaObject, ObjectNames.StandardDefaultSchema);
        keyColumns = AllTableKeys()
            .Where(key => key.Kind == "primary-key")
            .Where(key => SameName(key.TableName, tableName))
            .OrderBy(key => key.Ordinal)
            .ThenBy(key => key.ColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(key => key.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keyColumns.Length == 0)
        {
            return false;
        }

        return true;
    }

    private static bool HasUnsupportedTargetName(NamedTableReference target)
    {
        return target.SchemaObject.ServerIdentifier is not null;
    }

    private static bool HasMatchingParameters(
        IReadOnlyList<string> variables,
        IReadOnlyList<string> columns)
    {
        return columns.All(column => HasSingleMatchingParameter(variables, column));
    }

    private static bool HasSingleMatchingParameter(
        IReadOnlyList<string> variables,
        string columnName)
    {
        string expectedName = "@" + columnName;

        return variables.Count(variable => SameName(variable, expectedName)) == 1;
    }

    private sealed class DmlEvidenceVisitor(string rule) : TSqlFragmentVisitor
    {
        public List<DmlEvidence> Statements { get; } = [];

        private List<string> Variables { get; set; } = [];

        public override void Visit(UpdateStatement node)
        {
            if (rule == "update-without-where" && node.UpdateSpecification.WhereClause is null)
            {
                Statements.Add(new DmlEvidence(node, Variables.ToArray()));
            }

            base.Visit(node);
        }

        public override void Visit(DeleteStatement node)
        {
            if (rule == "delete-without-where" && node.DeleteSpecification.WhereClause is null)
            {
                Statements.Add(new DmlEvidence(node, Variables.ToArray()));
            }

            base.Visit(node);
        }

        public override void Visit(DeclareVariableStatement node)
        {
            Variables.AddRange(node.Declarations.Select(declaration => declaration.VariableName.Value));

            base.Visit(node);
        }

        public override void Visit(ProcedureStatementBody node)
        {
            List<string> outerVariables = Variables;
            Variables = [];

            base.Visit(node);

            Variables = outerVariables;
        }

        public override void Visit(ProcedureParameter node)
        {
            Variables.Add(node.VariableName.Value);

            base.Visit(node);
        }
    }

    private sealed record DmlEvidence(TSqlStatement Statement, IReadOnlyList<string> Variables);
}
