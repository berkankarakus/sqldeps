namespace SqlDeps;

/// <summary>An object a script defines: a table, view, procedure, function or trigger.</summary>
public sealed record DatabaseObject(
    string Name,
    string Kind,
    string SourceFile,
    int Line
);

/// <summary>One use of an object by another: "source_object does operation to target_object".</summary>
public sealed record Dependency(
    string SourceObject,
    string TargetObject,
    string Operation,
    string SourceFile,
    int Line,
    bool ViaDynamic = false
);

/// <summary>A procedure or function parameter declared in an indexed SQL file.</summary>
public sealed record ObjectParameter(
    string ObjectName,
    int Ordinal,
    string Name,
    string DataType,
    string? DefaultValue,
    bool IsOutput,
    string SourceFile
);

/// <summary>One column read or written by an indexed object when its table can be resolved.</summary>
public sealed record ColumnUsage(
    string SourceObject,
    string TableName,
    string ColumnName,
    string Operation,
    string SourceFile,
    int Line
);

/// <summary>A column declared by a CREATE TABLE statement.</summary>
public sealed record TableColumn(
    string TableName,
    string ColumnName,
    string DataType,
    string SourceFile,
    int Line,
    bool? IsNullable = null,
    bool IsIdentity = false,
    string? DefaultValue = null,
    bool IsComputed = false,
    int? Precision = null,
    int? Scale = null,
    string? Collation = null
);

/// <summary>A batch the parser could not read; its dependencies are missing from the index.</summary>
public sealed record ParseProblem(
    string SourceFile,
    int Line,
    string Message,
    int Column = 1
);

/// <summary>Non-SQL text skipped after a valid prefix, without losing the indexed SQL.</summary>
public sealed record IndexWarning(
    string SourceFile,
    int Line,
    string Message
);

/// <summary>A static T-SQL quality finding recorded while walking a parsed batch.</summary>
public sealed record LintFinding(
    string Rule,
    string Severity,
    string SourceObject,
    string SourceFile,
    int Line,
    int Column,
    int Length,
    string Message
);

/// <summary>A piece of a script between two GO lines, with the line it starts on.</summary>
public sealed record SqlBatch(
    string Text,
    int StartLine
);

/// <summary>A script file that was indexed, with the hash of its bytes.</summary>
public sealed record IndexedFile(
    string Path,
    string Hash
);
