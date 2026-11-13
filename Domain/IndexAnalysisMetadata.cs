namespace SqlDeps.Domain;

/// <summary>Indexed metadata needed to analyze an unsaved SQL document.</summary>
/// <param name="Catalog">Schema, key, foreign-key, and index metadata.</param>
/// <param name="Objects">Objects known to the selected index.</param>
/// <param name="Parameters">Indexed procedure and function parameters.</param>
/// <param name="DefaultDatabase">The selected index database context for unqualified live-document names.</param>
public sealed record IndexAnalysisMetadata(
    MetadataCatalog Catalog,
    IReadOnlyList<DatabaseObject> Objects,
    IReadOnlyList<ObjectParameter> Parameters,
    string? DefaultDatabase = null
);
