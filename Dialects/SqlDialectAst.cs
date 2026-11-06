namespace SqlDeps.Dialects;

public enum SqlDialectReferenceKind
{
    Table,
    Column,
}

public sealed record SqlDialectRange(
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter
);

public sealed record SqlDialectReference(
    string Name,
    SqlDialectReferenceKind Kind,
    SqlDialectRange Range
);

public sealed record SqlDialectStatement(
    string Kind,
    IReadOnlyList<SqlDialectReference> References
);

public sealed record SqlDialectParseError(
    string Message,
    SqlDialectRange Range
);

public sealed record SqlDialectParseResult(
    bool IsSuccess,
    IReadOnlyList<SqlDialectStatement> Statements,
    IReadOnlyList<SqlDialectParseError> Errors
);
