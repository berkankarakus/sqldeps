namespace SqlDeps.Core;

/// <summary>Indexed metadata needed to analyze an unsaved SQL document.</summary>
/// <param name="Catalog">Schema, key, foreign-key, and index metadata.</param>
/// <param name="Objects">Objects known to the selected index.</param>
/// <param name="Parameters">Indexed procedure and function parameters.</param>
public sealed record IndexAnalysisMetadata(
    MetadataCatalog Catalog,
    IReadOnlyList<DatabaseObject> Objects,
    IReadOnlyList<ObjectParameter> Parameters
);
