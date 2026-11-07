using System.Reflection;
using System.Text.Json;
using SqlDeps.Core;
using SqlDeps.Domain;
using SqlDeps.Lsp;
using SqlDeps.Storage;

namespace SqlDeps.Mcp;

/// <summary>Serves named, read-only index queries over the MCP stdio transport.</summary>
public static class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string UnknownVersion = "unknown";
    private const string DefaultSourceFile = "(mcp)";
    private const string JsonRpcVersion = "2.0";
    private const int JsonRpcParseErrorCode = -32700;
    private const int JsonRpcInvalidRequestCode = -32600;
    private const int JsonRpcMethodNotFoundCode = -32601;
    private const int JsonRpcInvalidParametersCode = -32602;
    private const int JsonRpcInternalErrorCode = -32603;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs the read-only MCP stdio server and returns a CLI-compatible exit code.</summary>
    public static int Run(string[] args)
    {
        try
        {
            var database = new IndexDatabase(ParseDatabasePath(args));
            var server = new McpServerSession(database);
            server.Run();
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static string ParseDatabasePath(string[] args)
    {
        string? databasePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] != "--db")
            {
                throw new ArgumentException("Unknown MCP option: " + args[index]);
            }

            if (++index == args.Length)
            {
                throw new ArgumentException("MCP option --db requires a file path.");
            }

            databasePath = Path.GetFullPath(args[index]);
        }

        return databasePath ?? IndexPathResolver.ActiveDatabasePathOrDefault();
    }

    private sealed class McpServerSession(IndexDatabase database)
    {
        private const int MaxAnalysisCharacters = 16 * 1024 * 1024;
        private const int MaxParameterizationCharacters = 1024 * 1024;
        private const int MaxCodeActions = 256;
        private readonly IndexQueryService queries = new(database);

        private readonly record struct TextRequest(string SourceFile, string Text);

        public void Run()
        {
            string? line;
            while ((line = Console.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Process(line);
            }
        }

        private void Process(string line)
        {
            JsonElement? requestId = null;

            try
            {
                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                requestId = GetRequestId(request);

                if (!IsValidRequest(request))
                {
                    WriteError(requestId, JsonRpcInvalidRequestCode, "Invalid Request.");

                    return;
                }

                var method = GetMethod(request);

                if (method is "notifications/initialized" or "notifications/cancelled")
                {
                    return;
                }

                if (requestId is null)
                {
                    return;
                }

                var result = Handle(method, GetParameters(request));
                WriteResponse(requestId.Value, result);
            }
            catch (JsonException exception)
            {
                WriteError(requestId, JsonRpcParseErrorCode, "Invalid JSON: " + exception.Message);
            }
            catch (MethodNotFoundException exception)
            {
                WriteError(requestId, JsonRpcMethodNotFoundCode, exception.Message);
            }
            catch (ArgumentException exception)
            {
                WriteError(requestId, JsonRpcInvalidParametersCode, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                WriteError(requestId, JsonRpcInternalErrorCode, exception.Message);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("sqldeps-mcp: request failed: " + exception.Message);
                WriteError(requestId, JsonRpcInternalErrorCode, "MCP internal error.");
            }
        }

        private object Handle(string method, JsonElement parameters)
        {
            return method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new { },
                "tools/list" => new { tools = McpToolCatalog.CreateDefinitions() },
                "resources/list" => ResourcesList(),
                "resources/read" => ReadResource(parameters),
                "tools/call" => CallTool(parameters),
                _ => throw new MethodNotFoundException("Unknown MCP method: " + method),
            };
        }

        private static object Initialize(JsonElement parameters)
        {
            var protocolVersion = parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("protocolVersion", out var requestedVersion)
                && requestedVersion.ValueKind == JsonValueKind.String
                ? requestedVersion.GetString() ?? ProtocolVersion
                : ProtocolVersion;

            return new
            {
                protocolVersion,
                capabilities = new
                {
                    tools = new { listChanged = false },
                    resources = new { subscribe = false, listChanged = false },
                },
                serverInfo = new
                {
                    name = "sqldeps",
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? UnknownVersion,
                },
            };
        }

        private static object ResourcesList()
        {
            var resources = DiagnosticRuleCatalog.All.Select(explanation => new
            {
                uri = DiagnosticResourceUri(explanation.Rule),
                name = explanation.Rule,
                description = explanation.Summary,
                mimeType = "application/json",
            });

            return new { resources = resources.ToArray() };
        }

        private static object ReadResource(JsonElement parameters)
        {
            var uri = McpArgumentReader.GetRequiredString(parameters, "uri");
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var resourceUri)
                || !string.Equals(resourceUri.Scheme, "sqldeps", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(resourceUri.Host, "diagnostic", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("MCP resource URI must use sqldeps://diagnostic/{rule}.");
            }

            var rule = resourceUri.AbsolutePath.Trim('/');
            if (rule.Length == 0 || !DiagnosticRuleCatalog.TryGet(rule, out var explanation))
            {
                throw new ArgumentException("Unknown diagnostic resource: " + rule);
            }

            var content = new
            {
                rule = explanation.Rule,
                summary = explanation.Summary,
                guidance = explanation.Guidance,
                default_severity = explanation.DefaultSeverity,
                references = explanation.References ?? [],
            };

            return new
            {
                contents = new[]
                {
                    new
                    {
                        uri,
                        mimeType = "application/json",
                        text = JsonSerializer.Serialize(content, JsonOptions),
                    },
                },
            };
        }

        private static string DiagnosticResourceUri(string rule) => "sqldeps://diagnostic/" + rule;

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
            AnalysisResult analysis = DocumentAnalysis.Analyze(sourceFile, text, database.DefaultSchema);
            IndexAnalysisMetadata indexed = queries.LoadAnalysisMetadata();
            IEnumerable<LintFinding> findings = analysis.LintFindings
                .Concat(QueryColumnValidator.Validate(
                    sourceFile,
                    analysis.ParsedBatches,
                    indexed.Catalog,
                    analysis.LocalColumns,
                    text))
                .Concat(ProcedureParameterValidator.Validate(
                    sourceFile,
                    analysis.ParsedBatches,
                    indexed.Objects,
                    indexed.Parameters,
                    text))
                .DistinctBy(finding => (finding.Rule, finding.Line, finding.Message));

            var diagnostics = findings
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
            var allActions = LanguageServer.PreviewCodeActions(
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
            var allCandidates = RefactorCandidatePreview.Build(
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

            if (!DynamicSqlParameterizer.TryParameterize(expression, declarations, out var replacement))
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

        private static JsonElement? GetRequestId(JsonElement request)
        {
            return request.ValueKind == JsonValueKind.Object
                && request.TryGetProperty("id", out var id)
                ? id.Clone()
                : null;
        }

        private static bool IsValidRequest(JsonElement request)
        {
            if (request.ValueKind != JsonValueKind.Object
                || !request.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String
                || !string.Equals(version.GetString(), JsonRpcVersion, StringComparison.Ordinal)
                || !request.TryGetProperty("method", out var method)
                || method.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(method.GetString()))
            {
                return false;
            }

            return !request.TryGetProperty("id", out var id)
                || id.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null;
        }

        private static string GetMethod(JsonElement request)
        {
            if (!request.TryGetProperty("method", out var method))
            {
                throw new ArgumentException("MCP request method is required.");
            }

            if (method.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("MCP request method is required.");
            }

            if (string.IsNullOrWhiteSpace(method.GetString()))
            {
                throw new ArgumentException("MCP request method is required.");
            }

            return method.GetString()!;
        }

        private static JsonElement GetParameters(JsonElement request)
        {
            return request.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
                ? parameters
                : default;
        }

        private static void WriteResponse(JsonElement requestId, object result)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = JsonRpcVersion, id = requestId, result }, JsonOptions));
        }

        private static void WriteError(JsonElement? requestId, int code, string message)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { jsonrpc = JsonRpcVersion, id = requestId, error = new { code, message } },
                JsonOptions
            ));
        }

        private sealed class MethodNotFoundException(string message) : Exception(message);
    }
}
