using SqlDeps.Domain;

namespace SqlDeps.Analysis;

/// <summary>Finds column references that can be checked against the indexed table schema.</summary>
internal static partial class QueryColumnValidator
{
    public static IEnumerable<LintFinding> Validate(
        string sourceFile,
        IEnumerable<ParsedSqlBatch> batches,
        MetadataCatalog metadata,
        IEnumerable<LocalColumn> localColumns,
        string? fullDocumentText = null,
        string? repositoryRoot = null
    )
    {
        var validationMetadata = QueryColumnValidationMetadata.Create(metadata);
        List<LintFinding> findings = [];

        foreach (var batch in batches)
        {
            findings.AddRange(ValidateBatch(sourceFile, batch, validationMetadata, localColumns, fullDocumentText, repositoryRoot));
        }

        return findings;
    }

    private static IEnumerable<LintFinding> ValidateBatch(
        string sourceFile,
        ParsedSqlBatch batch,
        QueryColumnValidationMetadata metadata,
        IEnumerable<LocalColumn> localColumns,
        string? fullDocumentText,
        string? repositoryRoot)
    {
        var batchEndLine = batch.StartLine + batch.Text.Count(character => character == '\n');
        var columnsByLocalName = localColumns
            .Where(column => column.Line >= batch.StartLine && column.Line <= batchEndLine)
            .GroupBy(column => column.Alias, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var queries = QuerySpecificationFinder.Find(batch.Script).ToArray();
        var cteNames = CteNameCollector.Collect(batch.Script);
        ColumnVisitor visitor = new(
            sourceFile,
            batch.StartLine,
            metadata.ColumnsByTable,
            metadata.MetadataByTable,
            metadata.ResultColumnsByObject,
            metadata.IndexedColumnsByTable,
            metadata.FilteredIndexColumnsByTable,
            metadata.ForeignKeysByColumn,
            columnsByLocalName,
            metadata.TablesByBaseName,
            queries,
            cteNames,
            CommonTableExpressionFinder.Find(batch.Script).ToArray());
        batch.Script.Accept(visitor);

        return LintSuppressions.Filter(
            visitor.Findings,
            batch.Text,
            batch.StartLine,
            fullDocumentText,
            repositoryRoot);
    }

}
