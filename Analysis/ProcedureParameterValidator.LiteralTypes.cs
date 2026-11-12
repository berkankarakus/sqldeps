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
            if (SqlDataTypeClassifier.IsBit(dataType))
            {
                return !CanConvertToBit(value);
            }

            if (SqlDataTypeClassifier.IsNumeric(dataType))
            {
                return !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            }

            if (dataType.StartsWith("UNIQUEIDENTIFIER", StringComparison.OrdinalIgnoreCase))
            {
                return !Guid.TryParse(value, out _);
            }

            if (SqlDataTypeClassifier.IsDateOrTime(dataType))
            {
                return !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _);
            }

            return false;
        }

        private static bool CanConvertToBit(string value)
        {
            return bool.TryParse(value, out _)
                || decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

    }
}
