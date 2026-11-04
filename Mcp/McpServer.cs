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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        private readonly IndexQueryService queries = new(database);

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
                WriteError(requestId, -32700, "Invalid JSON: " + exception.Message);
            }
            catch (MethodNotFoundException exception)
            {
                WriteError(requestId, -32601, exception.Message);
            }
            catch (ArgumentException exception)
            {
                WriteError(requestId, -32602, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                WriteError(requestId, -32603, exception.Message);
            }
        }

        private object Handle(string method, JsonElement parameters)
        {
            return method switch
            {
                "initialize" => Initialize(parameters),
                "ping" => new { },
                "tools/list" => new { tools = ToolDefinitions() },
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
                capabilities = new { tools = new { listChanged = false } },
                serverInfo = new
                {
                    name = "sqldeps",
                    version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                },
            };
        }

        private object CallTool(JsonElement parameters)
        {
            var toolName = GetRequiredString(parameters, "name");
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

            if (toolName == "parameterize_dynamic_sql")
            {
                return ParameterizeDynamicSql(arguments);
            }

            var rows = toolName switch
            {
                "find_objects" => queries.FindObjects(GetRequiredString(arguments, "pattern"), GetLimit(arguments)),
                "objects" => queries.ListQuery("objects", GetLimit(arguments)),
                "deps" => queries.ObjectQuery("deps", GetRequiredString(arguments, "object"), GetLimit(arguments)),
                "impact" => queries.ObjectQuery("impact", GetRequiredString(arguments, "object"), GetLimit(arguments)),
                "who_writes" => queries.ObjectQuery("who-writes", GetRequiredString(arguments, "object"), GetLimit(arguments)),
                "who_reads" => queries.ObjectQuery("who-reads", GetRequiredString(arguments, "object"), GetLimit(arguments)),
                "lint" => queries.Lint(GetLimit(arguments)),
                "table_columns" => queries.TableColumns(GetRequiredString(arguments, "table"), GetLimit(arguments)),
                "source" => queries.Source(GetRequiredString(arguments, "object")),
                "drift" => Drift(GetLimit(arguments)),
                _ => throw new ArgumentException("Unknown MCP tool: " + toolName),
            };

            return ToolResult(rows);
        }

        private object QueryBuilder(JsonElement arguments)
        {
            var tableName = GetRequiredString(arguments, "table");
            var includeJoin = GetOptionalBoolean(arguments, "join");
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
            var rule = GetRequiredString(arguments, "rule");
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
                },
            };
        }

        private object Analyze(JsonElement arguments)
        {
            string text = GetRequiredString(arguments, "text");
            if (text.Length > MaxAnalysisCharacters)
            {
                throw new ArgumentException($"MCP argument 'text' exceeds the {MaxAnalysisCharacters / (1024 * 1024)} MiB limit.");
            }

            string sourceFile = OptionalString(arguments, "source_file") ?? "(mcp)";
            AnalysisResult analysis = DocumentAnalysis.Analyze(sourceFile, text, database.DefaultSchema);
            IndexAnalysisMetadata indexed = queries.LoadAnalysisMetadata();
            IEnumerable<LintFinding> findings = analysis.LintFindings
                .Concat(QueryColumnValidator.Validate(
                    sourceFile,
                    analysis.ParsedBatches,
                    indexed.Catalog,
                    analysis.LocalColumns))
                .Concat(ProcedureParameterValidator.Validate(
                    sourceFile,
                    analysis.ParsedBatches,
                    indexed.Objects,
                    indexed.Parameters))
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

        private static object ParameterizeDynamicSql(JsonElement arguments)
        {
            string expression = GetRequiredString(arguments, "expression");
            string declarations = GetRequiredString(arguments, "declarations");

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

        private static object[] ToolDefinitions() =>
        [
            Tool("find_objects", "Find indexed object names without case sensitivity.", Properties(("pattern", "string"), ("limit", "integer")), ["pattern"]),
            Tool("objects", "List indexed SQL objects.", Properties(("limit", "integer")), []),
            Tool("source", "Find the source file and line for an indexed object.", Properties(("object", "string")), ["object"]),
            Tool("table_columns", "List indexed table or view columns, including primary and foreign key metadata.", Properties(("table", "string"), ("limit", "integer")), ["table"]),
            Tool("deps", "List direct dependencies of an indexed object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("impact", "List indexed objects affected by a change to an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("who_writes", "List indexed objects that write to an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("who_reads", "List indexed objects that read an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("lint", "List indexed parse, dependency, and SQL quality diagnostics.", Properties(("limit", "integer")), []),
            Tool("query_builder", "Build a read-only, parse-validated SELECT preview for an indexed table or view.", Properties(("table", "string"), ("join", "boolean")), ["table"]),
            Tool("explain", "Explain a supported sqldeps diagnostic rule and its suggested direction.", Properties(("rule", "string")), ["rule"]),
            Tool("analyze", "Analyze unsaved SQL with ScriptDom and active index metadata.", Properties(("text", "string"), ("source_file", "string")), ["text"]),
            Tool("parameterize_dynamic_sql", "Build a safe sp_executesql argument preview without writing files.", Properties(("expression", "string"), ("declarations", "string")), ["expression", "declarations"]),
            Tool("drift", "Compare the indexed source folder with its stored file hashes.", Properties(("limit", "integer")), []),
        ];

        private static object Tool(string name, string description, object properties, string[] required) => new
        {
            name,
            description,
            inputSchema = new
            {
                type = "object",
                properties,
                required,
            },
        };

        private static object Properties(params (string Name, string Type)[] fields)
        {
            var properties = fields.ToDictionary(
                field => field.Name,
                field => (object)new { type = field.Type, minimum = field.Type == "integer" ? 1 : (int?)null }
            );

            return properties;
        }

        private static int? GetLimit(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!arguments.TryGetProperty("limit", out var limit))
            {
                return null;
            }

            if (!limit.TryGetInt32(out var value))
            {
                throw new ArgumentException("MCP argument 'limit' must be a positive integer.");
            }

            if (value < 1)
            {
                throw new ArgumentException("MCP argument 'limit' must be a positive integer.");
            }

            return value;
        }

        private static bool GetOptionalBoolean(JsonElement arguments, string propertyName)
        {
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!arguments.TryGetProperty(propertyName, out var property))
            {
                return false;
            }

            if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new ArgumentException("MCP argument '" + propertyName + "' must be a boolean.");
            }

            return property.GetBoolean();
        }

        private static string GetRequiredString(JsonElement value, string propertyName)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("MCP argument '" + propertyName + "' is required.");
            }

            if (!value.TryGetProperty(propertyName, out var property))
            {
                throw new ArgumentException("MCP argument '" + propertyName + "' is required.");
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("MCP argument '" + propertyName + "' is required.");
            }

            if (string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new ArgumentException("MCP argument '" + propertyName + "' is required.");
            }

            return property.GetString()!;
        }

        private static string? OptionalString(JsonElement value, string propertyName)
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? text = property.GetString();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static JsonElement? GetRequestId(JsonElement request)
        {
            return request.TryGetProperty("id", out var id) ? id.Clone() : null;
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
            Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId, result }, JsonOptions));
        }

        private static void WriteError(JsonElement? requestId, int code, string message)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new { jsonrpc = "2.0", id = requestId, error = new { code, message } },
                JsonOptions
            ));
        }

        private sealed class MethodNotFoundException(string message) : Exception(message);
    }
}
