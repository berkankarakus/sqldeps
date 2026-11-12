using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Storage;

namespace SqlDeps.Preview;

/// <summary>Builds generated previews from named index metadata without putting SQL in adapters.</summary>
internal static class IndexPreviewService
{
    /// <summary>Builds a parse-validated read-only query preview for an indexed table or view.</summary>
    internal static QueryPreview BuildSelectPreview(
        IndexQueryService queries,
        string objectName,
        bool includeJoin = false)
    {
        string qualifiedName = queries.QualifyObjectName(objectName);
        IndexedObjectMetadata? source = queries.FindTableOrView(qualifiedName);

        if (source is null)
        {
            throw new ArgumentException($"No indexed table or view: {qualifiedName}", nameof(objectName));
        }

        IReadOnlyList<string> sourceColumns = queries.ColumnsFor(source.Name);

        if (!includeJoin)
        {
            return SqlQueryBuilder.BuildSelect(source.Name, source.Kind, sourceColumns);
        }

        IndexedForeignKey[] relations = queries.ForeignKeysFor(source.Name).ToArray();

        if (relations.Length != 1)
        {
            throw new ArgumentException(
                $"JOIN preview requires exactly one single-column foreign key for {qualifiedName}.",
                nameof(objectName));
        }

        IndexedForeignKey relation = relations[0];

        if (string.IsNullOrWhiteSpace(relation.ReferencedTableName)
            || string.IsNullOrWhiteSpace(relation.ReferencedColumnName)
            || string.IsNullOrWhiteSpace(relation.ColumnName))
        {
            throw new ArgumentException($"JOIN metadata is incomplete for {qualifiedName}.", nameof(objectName));
        }

        IndexedObjectMetadata? target = queries.FindTableOrView(relation.ReferencedTableName);

        if (target is null)
        {
            throw new ArgumentException(
                $"Referenced table is not indexed: {relation.ReferencedTableName}",
                nameof(objectName));
        }

        return SqlQueryBuilder.BuildSelectWithJoin(
            source.Name,
            source.Kind,
            sourceColumns,
            target.Name,
            queries.ColumnsFor(target.Name),
            relation.ColumnName,
            relation.ReferencedColumnName);
    }

    /// <summary>Builds a non-writing execution skeleton for an indexed procedure.</summary>
    internal static SqlTestSkeleton BuildTestSkeleton(IndexQueryService queries, string objectName)
    {
        string qualifiedName = queries.QualifyObjectName(objectName);
        IReadOnlyList<ObjectParameter> parameters = queries.ParametersForProcedure(qualifiedName);

        if (!queries.HasProcedure(qualifiedName))
        {
            throw new ArgumentException($"No indexed procedure: {qualifiedName}", nameof(objectName));
        }

        return SqlTestSkeletonBuilder.Build(qualifiedName, parameters);
    }
}
