namespace SqlDeps.Domain;

/// <summary>Evidence attached to a diagnostic explanation or experimental suggestion.</summary>
public sealed record DiagnosticReference(
    string Title,
    string Note,
    string Url
);
