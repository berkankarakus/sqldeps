using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    /// <summary>Reports only DML filters whose truth value is determined entirely by literals.</summary>
    private void ReportConstantDmlFilter(BooleanExpression condition, TSqlFragment statement)
    {
        var result = EvaluateCondition(condition);

        if (result is null)
        {
            ReportUnresolvedConstantFilter(condition, statement);

            return;
        }

        var rule = result.Value ? "dml-ineffective-filter" : "dml-always-false-filter";
        var message = result.Value
            ? "DML WHERE condition is always true and does not restrict the target rows."
            : "DML WHERE condition is always false and will not affect any rows.";
        AddLint(rule, "info", statement, message);
    }

    private void ReportUnresolvedConstantFilter(BooleanExpression condition, TSqlFragment statement)
    {
        if (!HasIneffectiveConstantBranch(condition))
        {
            return;
        }

        if (ContainsUnstableOperand(condition))
        {
            return;
        }

        AddLint(
            "dml-ineffective-filter",
            "info",
            statement,
            "DML WHERE condition contains a constant branch that does not restrict the target rows."
        );
    }

    private bool HasIneffectiveConstantBranch(BooleanExpression expression)
    {
        if (expression is not BooleanBinaryExpression binary)
        {
            return false;
        }

        var first = EvaluateCondition(binary.FirstExpression);
        var second = EvaluateCondition(binary.SecondExpression);
        var ineffective = IsIneffectiveBinaryBranch(binary, first, second);

        return ineffective
            || HasIneffectiveConstantBranch(binary.FirstExpression)
            || HasIneffectiveConstantBranch(binary.SecondExpression);
    }

    private static bool IsIneffectiveBinaryBranch(
        BooleanBinaryExpression binary,
        bool? first,
        bool? second)
    {
        if (binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
        {
            return first == true || second == true;
        }

        return first == false || second == false;
    }

    private static bool ContainsUnstableOperand(BooleanExpression expression)
    {
        UnstableOperandVisitor visitor = new();
        expression.Accept(visitor);

        return visitor.Found;
    }

    private sealed class UnstableOperandVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(VariableReference node)
        {
            Found = true;
        }

        public override void Visit(FunctionCall node)
        {
            if (IsNondeterministicFunction(node.FunctionName))
            {
                Found = true;
            }

            base.Visit(node);
        }

        private static bool IsNondeterministicFunction(Identifier? functionName)
        {
            if (functionName is null)
            {
                return false;
            }

            return NondeterministicFunctions.Contains(functionName.Value);
        }
    }
}
