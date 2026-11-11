using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Preview;

namespace SqlDeps.Mcp;

public static partial class McpServer
{
    private sealed partial class McpServerSession
    {
        private object CallTool(JsonElement parameters)
        {
            var toolName = McpArgumentReader.GetRequiredString(parameters, "name");
            var arguments = parameters.TryGetProperty("arguments", out var value)
                ? value
                : default;

            if (toolName == "query_builder")
            {
                return QueryBuilder(arguments);
            }

            if (toolName == "explain")
            {
                return Explain(arguments);
            }

            if (toolName == "analyze")
            {
                return Analyze(arguments);
            }

            if (toolName == "code_actions")
            {
                return CodeActions(arguments);
            }

            if (toolName == "refactor_candidates")
            {
                return RefactorCandidates(arguments);
            }

            if (toolName == "parameterize_dynamic_sql")
            {
                return ParameterizeDynamicSql(arguments);
            }

            if (toolName == "test_skeleton")
            {
                return TestSkeleton(arguments);
            }

            var rows = toolName switch
            {
                "find_objects" => queries.FindObjects(McpArgumentReader.GetRequiredString(arguments, "pattern"), McpArgumentReader.GetLimit(arguments)),
                "objects" => queries.ListQuery("objects", McpArgumentReader.GetLimit(arguments)),
                "deps" => queries.ObjectQuery("deps", McpArgumentReader.GetRequiredString(arguments, "object"), McpArgumentReader.GetLimit(arguments)),
                "impact" => queries.ObjectQuery("impact", McpArgumentReader.GetRequiredString(arguments, "object"), McpArgumentReader.GetLimit(arguments)),
                "who_writes" => queries.ObjectQuery("who-writes", McpArgumentReader.GetRequiredString(arguments, "object"), McpArgumentReader.GetLimit(arguments)),
                "who_reads" => queries.ObjectQuery("who-reads", McpArgumentReader.GetRequiredString(arguments, "object"), McpArgumentReader.GetLimit(arguments)),
                "who_updates" => queries.WhoUpdates(McpArgumentReader.GetRequiredString(arguments, "table"), McpArgumentReader.GetRequiredString(arguments, "column"), McpArgumentReader.GetLimit(arguments)),
                "lint" => queries.Lint(McpArgumentReader.GetLimit(arguments)),
                "table_columns" => queries.TableColumns(McpArgumentReader.GetRequiredString(arguments, "table"), McpArgumentReader.GetLimit(arguments)),
                "source" => queries.Source(McpArgumentReader.GetRequiredString(arguments, "object")),
                "drift" => Drift(McpArgumentReader.GetLimit(arguments)),
                _ => throw new ArgumentException("Unknown MCP tool: " + toolName),
            };

            return ToolResult(rows);
        }

        private object QueryBuilder(JsonElement arguments)
        {
            var tableName = McpArgumentReader.GetRequiredString(arguments, "table");
            var includeJoin = McpArgumentReader.GetOptionalBoolean(arguments, "join");
            var preview = queries.BuildSelectPreview(tableName, includeJoin);

            return new
            {
                content = new[] { new { type = "text", text = preview.Sql } },
                structuredContent = new
                {
                    object_name = preview.ObjectName,
                    object_kind = preview.ObjectKind,
                    columns = preview.Columns,
                    sql = preview.Sql,
                },
            };
        }

        private static object Explain(JsonElement arguments)
        {
            var rule = McpArgumentReader.GetRequiredString(arguments, "rule");
            if (!DiagnosticRuleCatalog.TryGet(rule, out var explanation))
            {
                throw new ArgumentException("Unknown diagnostic rule: " + rule);
            }

            return new
            {
                content = new[] { new { type = "text", text = explanation.Guidance } },
                structuredContent = new
                {
                    rule = explanation.Rule,
                    summary = explanation.Summary,
                    guidance = explanation.Guidance,
                    default_severity = explanation.DefaultSeverity,
                    references = explanation.References ?? [],
                },
            };
        }

        private object Analyze(JsonElement arguments)
        {
            string text = McpArgumentReader.GetRequiredString(arguments, "text");
            if (text.Length > MaxAnalysisCharacters)
            {
                throw new ArgumentException($"MCP argument 'text' exceeds the {MaxAnalysisCharacters / (1024 * 1024)} MiB limit.");
            }

            string? requestedSourceFile = McpArgumentReader.GetOptionalString(arguments, "source_file");
            string sourceFile = requestedSourceFile is null ? DefaultSourceFile : requestedSourceFile;
            IndexAnalysisMetadata indexed = queries.LoadAnalysisMetadata();
            DocumentDiagnosticResult analysis = DocumentDiagnosticService.Analyze(
                sourceFile,
                text,
                indexed,
                database.DefaultSchema);

            var diagnostics = analysis.Diagnostics
                .OrderBy(finding => finding.Line)
                .ThenBy(finding => finding.Column)
                .Select(finding => new
                {
                    severity = finding.Severity,
                    rule = finding.Rule,
                    object_name = finding.SourceObject,
                    line = finding.Line,
                    column = finding.Column,
                    length = finding.Length,
                    message = finding.Message,
                })
                .ToArray();
            var problems = analysis.Problems
                .Select(problem => new
                {
                    rule = "parse-error",
                    line = problem.Line,
                    column = problem.Column,
                    message = problem.Message,
                })
                .ToArray();

            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(new { diagnostics, problems }, JsonOptions),
                    },
                },
                structuredContent = new
                {
                    source_file = sourceFile,
                    diagnostics,
                    problems,
                },
            };
        }

        private object CodeActions(JsonElement arguments)
        {
            TextRequest request = ReadTextRequest(arguments);
            var allActions = PreviewService.CodeActions(
                queries.LoadAnalysisMetadata(),
                request.SourceFile,
                request.Text,
                database.DefaultSchema);
            var actions = allActions.Take(MaxCodeActions).ToArray();

            var result = new
            {
                source_file = request.SourceFile,
                actions,
                action_count = allActions.Length,
                truncated = allActions.Length > actions.Length,
                writes_file = false,
            };

            return new
            {
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) } },
                structuredContent = result,
            };
        }

        private object RefactorCandidates(JsonElement arguments)
        {
            TextRequest request = ReadTextRequest(arguments);
            var allCandidates = PreviewService.RefactorCandidates(
                queries.LoadAnalysisMetadata(),
                queries,
                request.SourceFile,
                request.Text,
                database.DefaultSchema);
            var candidates = allCandidates.Take(MaxCodeActions).ToArray();

            var result = new
            {
                source_file = request.SourceFile,
                candidates,
                candidate_count = allCandidates.Length,
                truncated = allCandidates.Length > candidates.Length,
                writes_file = false,
            };

            return new
            {
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) } },
                structuredContent = result,
            };
        }

        private static TextRequest ReadTextRequest(JsonElement arguments)
        {
            string text = McpArgumentReader.GetRequiredString(arguments, "text");

            if (text.Length > MaxAnalysisCharacters)
            {
                throw new ArgumentException($"MCP argument 'text' exceeds the {MaxAnalysisCharacters / (1024 * 1024)} MiB limit.");
            }

            string sourceFile = McpArgumentReader.GetOptionalString(arguments, "source_file") ?? DefaultSourceFile;

            return new TextRequest(sourceFile, text);
        }

        private static object ParameterizeDynamicSql(JsonElement arguments)
        {
            string expression = McpArgumentReader.GetRequiredString(arguments, "expression");
            string declarations = McpArgumentReader.GetRequiredString(arguments, "declarations");

            EnsureArgumentLength("expression", expression);
            EnsureArgumentLength("declarations", declarations);

            if (!DynamicSqlParameterizationService.TryParameterize(expression, declarations, out var replacement))
            {
                throw new ArgumentException("Dynamic SQL could not be safely parameterized from the supplied expression and declarations.");
            }

            return new
            {
                content = new[] { new { type = "text", text = replacement } },
                structuredContent = new
                {
                    replacement,
                    writes_file = false,
                },
            };
        }

        private object TestSkeleton(JsonElement arguments)
        {
            var skeleton = queries.BuildTestSkeleton(McpArgumentReader.GetRequiredString(arguments, "procedure"));

            return new
            {
                content = new[] { new { type = "text", text = skeleton.Sql } },
                structuredContent = new
                {
                    object_name = skeleton.ObjectName,
                    parameters = skeleton.Parameters,
                    sql = skeleton.Sql,
                    writes_file = false,
                },
            };
        }

        private static void EnsureArgumentLength(string name, string value)
        {
            if (value.Length <= MaxParameterizationCharacters)
            {
                return;
            }

            throw new ArgumentException($"MCP argument '{name}' exceeds the {MaxParameterizationCharacters / (1024 * 1024)} MiB limit.");
        }

        private List<Dictionary<string, object?>> Drift(int? limit)
        {
            var sourceRoot = database.SourceRoot;

            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                throw new InvalidOperationException("The active index has no recorded source folder.");
            }

            if (!Directory.Exists(sourceRoot))
            {
                throw new InvalidOperationException("Indexed source folder no longer exists: " + sourceRoot);
            }

            return database.GetDrift(sourceRoot)
                .Take(limit ?? int.MaxValue)
                .Select(item => new Dictionary<string, object?>
                {
                    ["state"] = item.State,
                    ["path"] = item.Path,
                })
                .ToList();
        }

        private static object ToolResult(IReadOnlyList<Dictionary<string, object?>> rows)
        {
            var text = JsonSerializer.Serialize(rows, JsonOptions);
            return new
            {
                content = new[] { new { type = "text", text } },
                structuredContent = new { rows },
            };
        }
    }
}
