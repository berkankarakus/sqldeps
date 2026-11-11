using System.Globalization;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlDeps;

internal static partial class ConstantExpressionEvaluator
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss",
        "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF",
        "yyyy'-'MM'-'dd HH':'mm':'ss",
        "yyyy'-'MM'-'dd HH':'mm':'ss.FFFFFFF",
    ];

    private static readonly string[] DateFormats =
    [
        "yyyy'-'MM'-'dd",
        "yyyyMMdd",
    ];

    private static readonly string[] TimeFormats =
    [
        "hh':'mm",
        "hh':'mm':'ss",
        "hh':'mm':'ss.FFFFFFF",
    ];

    private static bool? CompareTemporalValues(
        BooleanComparisonType comparisonType,
        TemporalValue first,
        TemporalValue second)
    {
        if (first.Kind != second.Kind
            && !(first.Kind is TemporalKind.Date or TemporalKind.DateTime
                && second.Kind is TemporalKind.Date or TemporalKind.DateTime))
        {
            return null;
        }

        int comparison = first.Kind == TemporalKind.Time || second.Kind == TemporalKind.Time
            ? first.Time.CompareTo(second.Time)
            : first.DateTime.CompareTo(second.DateTime);

        return comparisonType switch
        {
            BooleanComparisonType.Equals => comparison == 0,
            BooleanComparisonType.NotEqualToBrackets or BooleanComparisonType.NotEqualToExclamation => comparison != 0,
            BooleanComparisonType.GreaterThan => comparison > 0,
            BooleanComparisonType.LessThan => comparison < 0,
            BooleanComparisonType.GreaterThanOrEqualTo => comparison >= 0,
            BooleanComparisonType.LessThanOrEqualTo => comparison <= 0,
            BooleanComparisonType.NotLessThan => comparison >= 0,
            BooleanComparisonType.NotGreaterThan => comparison <= 0,
            _ => null,
        };
    }

    private static bool TryTemporalLiteralValue(ScalarExpression expression, out TemporalValue value)
    {
        value = default;

        if (expression is not StringLiteral literal)
        {
            return false;
        }

        if (DateTime.TryParseExact(
                literal.Value,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            value = new TemporalValue(dateTime, default, TemporalKind.DateTime);

            return true;
        }

        if (DateTime.TryParseExact(
                literal.Value,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            value = new TemporalValue(date, default, TemporalKind.Date);

            return true;
        }

        if (TimeSpan.TryParseExact(
                literal.Value,
                TimeFormats,
                CultureInfo.InvariantCulture,
                out var time))
        {
            value = new TemporalValue(default, time, TemporalKind.Time);

            return true;
        }

        return false;
    }

    private readonly record struct TemporalValue(DateTime DateTime, TimeSpan Time, TemporalKind Kind);

    private enum TemporalKind
    {
        Date,
        DateTime,
        Time,
    }
}
