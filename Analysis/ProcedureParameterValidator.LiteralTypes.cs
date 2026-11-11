using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Domain;

namespace SqlDeps.Analysis;

internal static partial class ProcedureParameterValidator
{
    private sealed partial class ProcedureVisitor
    {
        /// <summary>Reports literal arguments that cannot be converted to a known parameter type.</summary>
        private void ReportLiteralTypeMismatches(
            string procedureName,
            IReadOnlyList<ObjectParameter> knownParameters,
            ExecutableProcedureReference call)
        {
            foreach (var binding in BindArguments(knownParameters, call))
            {
                if (!TryGetInvalidLiteralBinding(binding, out var literal))
                {
                    continue;
                }

                var parameter = binding.Parameter!;
                Add("procedure-parameter-type", "warning", binding.Argument, $"Literal '{literal.Value}' cannot be converted to procedure parameter '{parameter.Name}' of type '{parameter.DataType}'.");
            }
        }

        private static bool TryGetInvalidLiteralBinding(ParameterBinding binding, out StringLiteral literal)
        {
            literal = null!;

            if (binding.Parameter is null)
            {
                return false;
            }

            if (binding.Argument.ParameterValue is not StringLiteral candidate)
            {
                return false;
            }

            literal = candidate;

            return CannotConvertLiteral(literal.Value, binding.Parameter.DataType);
        }

        private static bool CannotConvertLiteral(string value, string dataType)
        {
            if (IsNumericType(dataType))
            {
                return !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return !Guid.TryParse(value, out _);
            }

            if (dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
                || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase))
            {
                return !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
            }

            return false;
        }

        private static bool IsNumericType(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("MONEY", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("SMALLMONEY", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);
    }
}
