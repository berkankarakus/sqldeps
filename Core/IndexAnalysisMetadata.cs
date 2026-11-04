namespace SqlDeps.Core;

/// <summary>Indexed metadata needed to analyze an unsaved SQL document.</summary>
public sealed record IndexAnalysisMetadata(
    MetadataCatalog Catalog,
    IReadOnlyList<DatabaseObject> Objects,
    IReadOnlyList<ObjectParameter> Parameters
);
