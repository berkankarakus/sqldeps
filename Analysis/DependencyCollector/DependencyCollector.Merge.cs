using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps.Analysis;

internal sealed partial class DependencyCollector
{
    public override void Visit(MergeStatement node)
    {
        AddLint("merge", "warning", node, "MERGE has concurrency and correctness edge cases; verify its behavior under concurrent writes.");
        var specification = node.MergeSpecification;
        RecordWriteTarget(specification.Target, null, "MERGE");

        if (HasUnorderedTopInMergeSource(specification.TableReference))
        {
            AddLint("merge-top-without-order", "warning", specification.TableReference, "MERGE source uses TOP without ORDER BY; source row selection is nondeterministic.");
        }

        var matchResult = EvaluateCondition(specification.SearchCondition);

        if (matchResult is false)
        {
            AddLint(
                "dml-always-false-filter",
                "info",
                node,
                "MERGE match condition is always false and will not match any target rows."
            );
        }
        else
        {
            if (matchResult is true)
            {
                AddLint(
                    "dml-ineffective-filter",
                    "info",
                    node,
                    "MERGE match condition is always true and does not restrict target/source matching."
                );
            }

            if (ShouldReportNondeterministicMerge(specification.SearchCondition))
            {
                AddLint("merge-nondeterministic-match", "warning", (TSqlFragment?)specification.SearchCondition ?? node, "MERGE match condition has no equality between two column references; verify that one source row cannot match multiple target rows.");
            }
        }

        foreach (var clause in specification.ActionClauses)
        {
            ReportMergeActionCondition(clause);

            var operation = MergeOperationOf(clause.Action);

            if (operation is not null)
            {
                RecordWriteTarget(specification.Target, null, operation);
            }

            if (clause.Action is UpdateMergeAction update)
            {
                ReportDuplicateMergeAssignments(update);
            }
        }
    }

    private void ReportMergeActionCondition(MergeActionClause clause)
    {
        if (clause.SearchCondition is not { } condition
            || EvaluateCondition(condition) is not { } result)
        {
            return;
        }

        var rule = result ? "dml-ineffective-filter" : "dml-always-false-filter";
        var message = result
            ? "MERGE action condition is always true and does not restrict this action."
            : "MERGE action condition is always false and this action will never execute.";
        AddLint(rule, "info", clause, message);
    }

    private static bool ShouldReportNondeterministicMerge(BooleanExpression searchCondition)
    {
        var constantResult = ConstantExpressionEvaluator.Evaluate(searchCondition);

        if (constantResult is false)
        {
            return false;
        }

        return !HasDeterministicMergeMatch(searchCondition);
    }

    private static bool HasUnorderedTopInMergeSource(TableReference source) => source switch
    {
        QueryDerivedTable derived => HasUnorderedTop(derived.QueryExpression),
        _ => false,
    };

    private static bool HasUnorderedTop(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification.TopRowFilter is not null && specification.OrderByClause is null,
        QueryParenthesisExpression parenthesized => HasUnorderedTop(parenthesized.QueryExpression),
        BinaryQueryExpression binary => HasUnorderedTop(binary.FirstQueryExpression)
            || HasUnorderedTop(binary.SecondQueryExpression),
        _ => false,
    };

    private void ReportDuplicateMergeAssignments(UpdateMergeAction update)
    {
        foreach (var assignments in update.SetClauses
            .OfType<AssignmentSetClause>()
            .Where(assignment => assignment.Column?.MultiPartIdentifier?.Identifiers.Count > 0)
            .GroupBy(assignment => assignment.Column!.MultiPartIdentifier!.Identifiers[^1].Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var assignment in assignments.Skip(1))
            {
                AddLint("duplicate-merge-column", "error", assignment.Column!, $"Column '{assignments.Key}' is assigned more than once in this MERGE action.");
            }
        }
    }

    private static bool HasDeterministicMergeMatch(BooleanExpression? condition)
    {
        if (condition is null)
        {
            return false;
        }

        MergeMatchVisitor visitor = new();
        condition.Accept(visitor);

        return visitor.HasColumnEquality;
    }

    private static string? MergeOperationOf(MergeAction action)
    {
        if (action is UpdateMergeAction)
        {
            return "UPDATE";
        }

        if (action is InsertMergeAction)
        {
            return "INSERT";
        }

        if (action is DeleteMergeAction)
        {
            return "DELETE";
        }

        return null;
    }

    private sealed class MergeMatchVisitor : TSqlFragmentVisitor
    {
        public bool HasColumnEquality { get; private set; }

        public override void Visit(BooleanComparisonExpression node)
        {
            if (IsColumnEquality(node))
            {
                HasColumnEquality = true;
            }

            base.Visit(node);
        }

        private static bool IsColumnEquality(BooleanComparisonExpression node)
        {
            if (node.ComparisonType != BooleanComparisonType.Equals)
            {
                return false;
            }

            if (node.FirstExpression is not ColumnReferenceExpression left
                || node.SecondExpression is not ColumnReferenceExpression right)
            {
                return false;
            }

            return !string.Equals(Qualifier(left), Qualifier(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string? Qualifier(ColumnReferenceExpression column) => column.MultiPartIdentifier?.Identifiers.Count > 1
            ? column.MultiPartIdentifier.Identifiers[^2].Value
            : null;
    }
}
