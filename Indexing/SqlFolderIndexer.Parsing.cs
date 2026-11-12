using System.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlDeps.Analysis;
using SqlDeps.Domain;
using SqlDeps.Indexing;

namespace SqlDeps;

public sealed partial class SqlFolderIndexer
{
    private void IndexBatch(
        SqlBatch batch,
        string relativePath,
        TSql160Parser parser,
        IndexedFileContribution result,
        string fullSourceText,
        string repositoryRoot
    )
    {
        var parseStarted = Stopwatch.GetTimestamp();
        var script = ParseBatch(batch, parser, out var errors);
        result.ParseTicks += Stopwatch.GetTimestamp() - parseStarted;

        if (errors.Count > 0)
        {
            HandleParseErrors(batch, relativePath, errors, fullSourceText, repositoryRoot, result);

            return;
        }

        if (script is null)
        {
            return;
        }

        var collectStarted = Stopwatch.GetTimestamp();
        CollectScript(script, batch.Text, fullSourceText, relativePath, batch.StartLine, repositoryRoot, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;
    }

    private void HandleParseErrors(
        SqlBatch batch,
        string relativePath,
        IList<ParseError> errors,
        string fullSourceText,
        string repositoryRoot,
        IndexedFileContribution result
    )
    {
        if (!IsUserScript(relativePath))
        {
            AddParseErrors(batch, relativePath, errors, result);

            return;
        }

        if (TryIndexValidPrefix(batch, relativePath, errors[0].Line, fullSourceText, repositoryRoot, result))
        {
            result.Warnings.Add(new IndexWarning(
                relativePath,
                batch.StartLine + errors[0].Line - 1,
                "Non-SQL text after a valid SQL prefix was skipped."));

            return;
        }

        AddParseErrors(batch, relativePath, errors, result);
    }

    private static TSqlScript? ParseBatch(SqlBatch batch, TSql160Parser parser, out IList<ParseError> errors)
    {
        using StringReader reader = new(batch.Text);
        var fragment = parser.Parse(reader, out errors);

        return fragment as TSqlScript;
    }

    private static void AddParseErrors(
        SqlBatch batch,
        string relativePath,
        IEnumerable<ParseError> errors,
        IndexedFileContribution result
    )
    {
        foreach (var error in errors)
        {
            result.Problems.Add(new ParseProblem(relativePath, batch.StartLine + error.Line - 1, error.Message));
        }
    }

    private bool TryIndexValidPrefix(
        SqlBatch batch,
        string relativePath,
        int errorLine,
        string fullSourceText,
        string repositoryRoot,
        IndexedFileContribution result)
    {
        var prefix = string.Join('\n', batch.Text.Split('\n').Take(errorLine - 1));

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        TSql160Parser parser = new(initialQuotedIdentifiers: true);
        using StringReader reader = new(prefix);
        var parseStarted = Stopwatch.GetTimestamp();
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        result.ParseTicks += Stopwatch.GetTimestamp() - parseStarted;

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            return false;
        }

        var collectStarted = Stopwatch.GetTimestamp();
        CollectScript(script, prefix, fullSourceText, relativePath, batch.StartLine, repositoryRoot, result);
        result.CollectTicks += Stopwatch.GetTimestamp() - collectStarted;

        return true;
    }

    private void CollectScript(
        TSqlScript script,
        string sourceText,
        string fullSourceText,
        string relativePath,
        int batchStartLine,
        string? repositoryRoot,
        IndexedFileContribution result
    )
    {
        DependencyCollectionAnalysis collection = DependencyCollectionAnalyzer.Collect(
            script,
            relativePath,
            batchStartLine,
            defaultSchema,
            sourceText);
        MergeCollectorResult(result, collection, fullSourceText, batchStartLine, repositoryRoot);
    }

    private static void MergeCollectorResult(
        IndexedFileContribution result,
        DependencyCollectionAnalysis collection,
        string fullSourceText,
        int batchStartLine,
        string? repositoryRoot
    )
    {
        DependencyCollectionResult contribution = collection.Contribution;
        result.GenerateScriptTicks += collection.GenerateScriptTicks;
        result.GenerateScriptCalls += collection.GenerateScriptCalls;
        result.SourceSliceHits += collection.SourceSliceHits;
        result.Objects.AddRange(contribution.Objects);
        result.Dependencies.AddRange(contribution.Dependencies);
        result.Parameters.AddRange(contribution.Parameters);
        result.ResultColumns.AddRange(contribution.ResultColumns);
        result.TableKeys.AddRange(contribution.TableKeys);
        result.ForeignKeys.AddRange(contribution.ForeignKeys);
        result.IndexColumns.AddRange(contribution.IndexColumns);
        result.ColumnUsage.AddRange(contribution.ColumnUsage);
        result.TableColumns.AddRange(contribution.TableColumns);
        result.Problems.AddRange(contribution.Problems);
        result.LintFindings.AddRange(LintSuppressions.Filter(
            contribution.LintFindings,
            fullSourceText,
            batchStartLine,
            fullSourceText,
            repositoryRoot));
    }

    private static bool IsUserScript(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => string.Equals(part, "UserScripts", StringComparison.OrdinalIgnoreCase));
    }

}
