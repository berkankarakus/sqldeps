using SqlDeps.Cli;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Preview;
using SqlDeps.Storage;

namespace SqlDeps;

public static partial class CommandLine
{
    private static int Status(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var folder = ExistingFolder(options.Value!);
        var status = new IndexDatabase(options.DatabasePath ?? DefaultDatabasePath(folder)).GetStatus(folder);
        ConsoleOutput.WriteStatus(status, options.Json, !options.NoColor);
        return status.IsCurrent ? 0 : 1;
    }

    private static int ObjectQuery(string command, string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var objectName = database.QualifyObjectName(options.Value!);
        var rows = new IndexQueryService(database).ObjectQuery(command, objectName, options.Limit);
        if (options.Table) ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        else ConsoleOutput.WriteObjectRows(command, objectName, rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int WhoUpdates(string[] args)
    {
        var options = Parse(args, requireValue: true, maximumValues: 2);
        if (options.SecondValue is null) throw new ArgumentException(Usage);
        var database = OpenDatabase(options.DatabasePath);
        var tableName = database.QualifyObjectName(options.Value!);
        var rows = new IndexQueryService(database).WhoUpdates(tableName, options.SecondValue, options.Limit);
        var target = tableName + "." + options.SecondValue;
        if (options.Table) ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        else ConsoleOutput.WriteObjectRows("who-updates", target, rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int Source(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var rows = new IndexQueryService(database).Source(options.Value!);
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int Graph(string[] args)
    {
        var options = Parse(args, requireValue: false, allowGraphFormat: true);
        var rows = new IndexQueryService(OpenDatabase(options.DatabasePath)).GraphEdges(options.Limit);
        string format = options.GraphFormat ?? "json";
        ConsoleOutput.WriteGraph(rows, format);

        return 0;
    }

    private static int Find(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var rows = new IndexQueryService(OpenDatabase(options.DatabasePath)).FindObjects(options.Value!, options.Limit);
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int TableColumns(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var database = OpenDatabase(options.DatabasePath);
        var rows = new IndexQueryService(database).TableColumns(options.Value!, options.Limit);
        if (!options.Json)
        {
            rows = rows.Select(AddKeyMarkers).ToList();
        }

        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int QueryBuilder(string[] args)
    {
        var options = Parse(args, requireValue: true, allowJoin: true);
        var preview = new IndexQueryService(OpenDatabase(options.DatabasePath)).BuildSelectPreview(options.Value!, options.Join);
        ConsoleOutput.WriteQueryPreview(preview, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int TestSkeleton(string[] args)
    {
        var options = Parse(args, requireValue: true);
        var skeleton = new IndexQueryService(OpenDatabase(options.DatabasePath)).BuildTestSkeleton(options.Value!);
        ConsoleOutput.WriteTestSkeleton(skeleton, options.Json, !options.NoColor, !options.NoPager);

        return 0;
    }

    private static int RefactorCandidates(string[] args)
    {
        var options = Parse(args, requireValue: true, allowSchema: true);
        var sourceFile = Path.GetFullPath(options.Value!);

        if (!File.Exists(sourceFile))
        {
            throw new ArgumentException("SQL file does not exist: " + sourceFile);
        }

        var database = OpenDatabase(options.DatabasePath);
        var queries = new IndexQueryService(database);
        string defaultSchema = options.DefaultSchema is null ? database.DefaultSchema : options.DefaultSchema;
        var candidates = PreviewService.RefactorCandidates(
            queries.LoadAnalysisMetadata(),
            queries,
            sourceFile,
            File.ReadAllText(sourceFile),
            defaultSchema);

        ConsoleOutput.WriteRefactorCandidates(
            sourceFile,
            candidates,
            options.Json,
            !options.NoColor,
            !options.NoPager);

        return 0;
    }

    private static Dictionary<string, object?> AddKeyMarkers(Dictionary<string, object?> row)
    {
        var columnName = row.TryGetValue("column_name", out var name) ? name?.ToString() ?? "" : "";
        var primaryKey = row.TryGetValue("primary_key", out var primary) ? primary?.ToString() : null;
        var foreignKey = row.TryGetValue("foreign_key", out var foreign) ? foreign?.ToString() : null;
        var markers = new[]
        {
            string.Equals(primaryKey, "PK", StringComparison.OrdinalIgnoreCase) ? "[PK]" : null,
            string.IsNullOrWhiteSpace(foreignKey) ? null : $"[FK → {foreignKey}]",
        }.OfType<string>();

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in row)
        {
            if (pair.Key is "primary_key" or "foreign_key")
            {
                continue;
            }

            result[pair.Key] = pair.Key == "column_name"
                ? string.Join(" ", new[] { columnName }.Concat(markers))
                : pair.Value;
        }

        return result;
    }

    private static int ListQuery(string command, string[] args)
    {
        var options = Parse(args, requireValue: false);
        var rows = new IndexQueryService(OpenDatabase(options.DatabasePath)).ListQuery(command, options.Limit);
        ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        return 0;
    }

    private static int Lint(string[] args)
    {
        var options = Parse(args, requireValue: false, allowFailOn: true, allowSarif: true, allowProfile: true);
        var profile = DiagnosticProfileCatalog.Get(options.Profile);
        var database = OpenDatabase(options.DatabasePath);
        var rows = new IndexQueryService(database)
            .Lint(options.Limit)
            .Where(row => profile.Includes(row.TryGetValue("rule", out var rule) ? rule?.ToString() ?? "" : ""))
            .ToList();
        if (options.Json || options.Sarif)
        {
            rows = AddLintMetadata(database, rows);
        }

        if (options.Sarif)
        {
            ConsoleOutput.WriteSarif(rows, profile.Name);
        }
        else
        {
            ConsoleOutput.WriteRows(rows, options.Json, !options.NoColor, !options.NoPager);
        }

        return options.FailOn is null || !rows.Any(row => MeetsFailThreshold(row, options.FailOn)) ? 0 : 1;
    }

    private static int Explain(string[] args)
    {
        var options = Parse(args, requireValue: true);

        if (!DiagnosticRuleCatalog.TryGet(options.Value!, out var explanation))
        {

            throw new ArgumentException("Unknown diagnostic rule: " + options.Value!);
        }

        ConsoleOutput.WriteDiagnosticExplanation(explanation, options.Json, !options.NoColor, !options.NoPager);


        return 0;
    }

    private static List<Dictionary<string, object?>> AddLintMetadata(IndexDatabase database, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var source = "fresh-index";
        if (database.SourceRoot is { } root && Directory.Exists(root)) source = database.GetStatus(root).IsCurrent ? "fresh-index" : "stale-index";
        return rows.Select(row =>
        {
            var rule = row.TryGetValue("rule", out var ruleValue) ? ruleValue?.ToString() : null;
            var severity = row.TryGetValue("severity", out var severityValue) ? severityValue?.ToString() : null;
            var metadata = DiagnosticMetadataMapper.For(source, rule, severity);
            var result = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = metadata.Source,
                ["confidence"] = metadata.Confidence,
                ["stale"] = metadata.IsStale,
            };
            if (metadata.ConfidenceReason is { } confidenceReason) result["confidenceReason"] = confidenceReason;
            return result;
        }).ToList();
    }

    private static bool MeetsFailThreshold(IReadOnlyDictionary<string, object?> row, string threshold)
    {
        var severity = row.TryGetValue("severity", out var value) ? value?.ToString() : null;
        return SeverityRank(severity) >= SeverityRank(threshold);
    }

    private static int SeverityRank(string? severity) => severity?.ToLowerInvariant() switch
    {
        "info" => 1,
        "warning" => 2,
        "error" => 3,
        _ => 0,
    };

}
