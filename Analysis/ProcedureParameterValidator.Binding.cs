using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class ProcedureParameterValidator
{
    private sealed partial class ProcedureVisitor
    {
        private void ReportMissingRequiredParameters(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call)
        {
            var bindings = BindArguments(knownParameters, call);
            var positionalCount = bindings.Count(binding => binding.Argument.Variable is null);
            var suppliedNames = bindings
                .Where(binding => binding.Parameter is not null)
                .Select(binding => binding.Parameter!.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var required in knownParameters.Where(parameter => IsMissingRequiredParameter(parameter, positionalCount, suppliedNames)))
            {
                Add("missing-procedure-parameter", "warning", call, $"Procedure '{procedureName}' requires parameter '{required.Name}'.");
            }
        }

        private static bool IsMissingRequiredParameter(
            ObjectParameter parameter,
            int positionalCount,
            IReadOnlySet<string> suppliedNames)
        {
            return parameter.DefaultValue is null
                && parameter.Ordinal > positionalCount
                && !suppliedNames.Contains(parameter.Name);
        }

        private void ReportInvalidDefaultArguments(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call)
        {
            foreach (var binding in BindArguments(knownParameters, call))
            {
                if (HasInvalidDefault(binding))
                {
                    Add("invalid-procedure-default", binding.Argument, $"Procedure '{procedureName}' has no default value for parameter '{binding.Parameter!.Name}'.");
                }
            }
        }

        private static bool HasInvalidDefault(ParameterBinding binding)
        {
            if (binding.Parameter is null)
            {
                return false;
            }

            if (binding.Argument.ParameterValue is not DefaultLiteral)
            {
                return false;
            }

            return binding.Parameter.DefaultValue is null;
        }

        private static ParameterBinding[] BindArguments(
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call)
        {
            var positionalIndex = 0;
            List<ParameterBinding> bindings = [];

            foreach (var argument in call.Parameters)
            {
                var parameter = FindParameter(knownParameters, argument, positionalIndex);

                if (argument.Variable is null)
                {
                    positionalIndex++;
                }

                bindings.Add(new ParameterBinding(argument, parameter));
            }

            return bindings.ToArray();
        }

        private static ObjectParameter? FindParameter(
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecuteParameter argument,
            int positionalIndex)
        {
            if (argument.Variable is not null)
            {
                return knownParameters.FirstOrDefault(item => string.Equals(
                    item.Name,
                    argument.Variable.Name,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (positionalIndex >= knownParameters.Count)
            {
                return null;
            }

            return knownParameters[positionalIndex];
        }

        private sealed record ParameterBinding(ExecuteParameter Argument, ObjectParameter? Parameter);
    }
}
