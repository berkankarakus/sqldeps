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
    int Line
);

/// <summary>A batch the parser could not read; its dependencies are missing from the index.</summary>
public sealed record ParseProblem(
    string SourceFile,
    int Line,
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
