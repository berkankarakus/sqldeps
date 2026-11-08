using System.Text;
using System.Text.Json;
using SqlDeps.Domain;

namespace SqlDeps;

/// <summary>Identifies one reusable batch contribution in the persistent index cache.</summary>
public sealed record CachedBatch(
    string SourceFile,
    int Ordinal,
    int StartLine,
    string TextHash,
    string Payload
);

/// <summary>Serializes batch contributions without coupling the SQLite schema to every model type.</summary>
internal static class BatchContributionCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public static string HashOf(string text)
    {

        return SqlFileReader.HashOf(Encoding.UTF8.GetBytes(text));
    }

    public static string Serialize(IndexedFileContribution contribution)
    {
        BatchContributionPayload payload = new(
            contribution.Objects,
            contribution.Dependencies,
            contribution.Parameters,
            contribution.ResultColumns,
            contribution.TableKeys,
            contribution.ForeignKeys,
            contribution.IndexColumns,
            contribution.ColumnUsage,
            contribution.TableColumns,
            contribution.Problems,
            contribution.Warnings,
            contribution.LintFindings);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static bool Matches(
        CachedBatch cached,
        string sourceFile,
        int ordinal,
        int startLine,
        string text)
    {
        string textHash = HashOf(text);

        return string.Equals(cached.SourceFile, sourceFile, StringComparison.Ordinal)
            && cached.Ordinal == ordinal
            && cached.StartLine == startLine
            && string.Equals(cached.TextHash, textHash, StringComparison.Ordinal);
    }

    public static bool TryDeserialize(
        CachedBatch batch,
        IndexedFile file,
        out IndexedFileContribution? contribution)
    {
        try
        {
            contribution = Deserialize(batch, file);

            return true;
        }
        catch (JsonException)
        {
            contribution = null;

            return false;
        }
        catch (InvalidDataException)
        {
            contribution = null;

            return false;
        }
    }

    public static IndexedFileContribution Deserialize(CachedBatch batch, IndexedFile file)
    {
        BatchContributionPayload payload = JsonSerializer.Deserialize<BatchContributionPayload>(
            batch.Payload,
            JsonOptions) ?? throw new InvalidDataException("Batch cache payload is empty.");
        IndexedFileContribution contribution = new(file);
        contribution.Objects.AddRange(payload.Objects);
        contribution.Dependencies.AddRange(payload.Dependencies);
        contribution.Parameters.AddRange(payload.Parameters);
        contribution.ResultColumns.AddRange(payload.ResultColumns);
        contribution.TableKeys.AddRange(payload.TableKeys);
        contribution.ForeignKeys.AddRange(payload.ForeignKeys);
        contribution.IndexColumns.AddRange(payload.IndexColumns);
        contribution.ColumnUsage.AddRange(payload.ColumnUsage);
        contribution.TableColumns.AddRange(payload.TableColumns);
        contribution.Problems.AddRange(payload.Problems);
        contribution.Warnings.AddRange(payload.Warnings);
        contribution.LintFindings.AddRange(payload.LintFindings);
        contribution.MarkReused();

        return contribution;
    }

    private sealed record BatchContributionPayload(
        IReadOnlyList<DatabaseObject> Objects,
        IReadOnlyList<Dependency> Dependencies,
        IReadOnlyList<ObjectParameter> Parameters,
        IReadOnlyList<ResultColumn> ResultColumns,
        IReadOnlyList<TableKey> TableKeys,
        IReadOnlyList<TableForeignKey> ForeignKeys,
        IReadOnlyList<TableIndexColumn> IndexColumns,
        IReadOnlyList<ColumnUsage> ColumnUsage,
        IReadOnlyList<TableColumn> TableColumns,
        IReadOnlyList<ParseProblem> Problems,
        IReadOnlyList<IndexWarning> Warnings,
        IReadOnlyList<LintFinding> LintFindings
    );
}
