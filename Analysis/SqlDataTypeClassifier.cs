namespace SqlDeps.Analysis;

internal static class SqlDataTypeClassifier
{
    public static bool IsBit(string dataType) => dataType.StartsWith("BIT", StringComparison.OrdinalIgnoreCase);

    public static bool IsNumeric(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase)
        || IsBit(dataType)
        || dataType.StartsWith("DECIMAL", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("NUMERIC", StringComparison.OrdinalIgnoreCase)
        || IsMoney(dataType)
        || dataType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("REAL", StringComparison.OrdinalIgnoreCase);

    public static bool IsInteger(string dataType) => dataType.StartsWith("INT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("BIGINT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("SMALLINT", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("TINYINT", StringComparison.OrdinalIgnoreCase);

    public static bool IsMoney(string dataType) => dataType.StartsWith("MONEY", StringComparison.OrdinalIgnoreCase)
        || dataType.StartsWith("SMALLMONEY", StringComparison.OrdinalIgnoreCase);

    public static bool IsDateOrTime(string dataType)
    {
        return dataType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDateTime(string dataType)
    {
        return dataType.StartsWith("DATETIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("DATETIMEOFFSET", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsString(string dataType)
    {
        return dataType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("NCHAR", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("NVARCHAR", StringComparison.OrdinalIgnoreCase)
            || dataType.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase);
    }
}
