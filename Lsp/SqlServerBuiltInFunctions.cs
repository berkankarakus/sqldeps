namespace SqlDeps.Lsp;

/// <summary>Names handled by SQL Server rather than workspace-defined functions.</summary>
internal static class SqlServerBuiltInFunctions
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "ABS", "ACOS", "ASIN", "ATAN", "ATN2", "AVG", "CEILING", "CHAR", "CHARINDEX",
        "CHECKSUM", "CHECKSUM_AGG", "COALESCE", "CONCAT", "CONCAT_WS", "COUNT", "COS", "COT",
        "CURRENT_DATE", "CURRENT_TIMESTAMP", "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME",
        "DATEPART", "DATEFROMPARTS", "DATETIME2FROMPARTS", "DATETIMEFROMPARTS", "DATETIMEOFFSETFROMPARTS",
        "DAY", "DEGREES", "DENSE_RANK", "EOMONTH", "EXP", "FORMAT", "FLOOR", "GETDATE",
        "GETUTCDATE", "GROUPING", "GROUPING_ID", "IIF", "ISDATE", "ISJSON", "ISNULL", "ISNUMERIC", "JSON_MODIFY",
        "JSON_QUERY", "JSON_VALUE", "LAG", "LAST_VALUE", "LEAD", "LEFT", "LEN", "LOG", "LOG10",
        "LOWER", "LTRIM", "MAX", "MIN", "MONTH", "NCHAR", "NULLIF", "NTILE", "OBJECT_ID", "PATINDEX",
        "PERCENT_RANK", "PI", "POWER", "RADIANS", "RANK", "RAND", "REPLACE", "REPLICATE",
        "REVERSE", "RIGHT", "ROUND", "ROW_NUMBER", "RTRIM", "SIGN", "SIN", "SOUNDEX", "SPACE",
        "SQRT", "SQUARE", "STDEV", "STDEVP", "STRING_AGG", "STRING_ESCAPE", "STUFF", "SUBSTRING",
        "SUM", "SYSDATETIME", "SYSDATETIMEOFFSET", "SYSUTCDATETIME", "TAN", "TRANSLATE", "TRIM",
        "TRY_CONVERT", "TRY_PARSE", "UNICODE", "UPPER", "VAR", "VARP", "YEAR"
    };

    public static bool Contains(string functionName)
    {
        var baseName = functionName.Split('.').Last();
        return Names.Contains(baseName);
    }
}
