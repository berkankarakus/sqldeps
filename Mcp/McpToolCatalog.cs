namespace SqlDeps.Mcp;

internal static class McpToolCatalog
{
    internal static object[] CreateDefinitions()
    {
        return
        [
            Tool("find_objects", "Find indexed object names without case sensitivity.", Properties(("pattern", "string"), ("limit", "integer")), ["pattern"]),
            Tool("objects", "List indexed SQL objects.", Properties(("limit", "integer")), []),
            Tool("source", "Find the source file and line for an indexed object.", Properties(("object", "string")), ["object"]),
            Tool("table_columns", "List indexed table or view columns, including primary and foreign key metadata.", Properties(("table", "string"), ("limit", "integer")), ["table"]),
            Tool("deps", "List direct dependencies of an indexed object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("impact", "List indexed objects affected by a change to an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("who_writes", "List indexed objects that write to an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("who_reads", "List indexed objects that read an object.", Properties(("object", "string"), ("limit", "integer")), ["object"]),
            Tool("who_updates", "List indexed objects that update a specific table column.", Properties(("table", "string"), ("column", "string"), ("limit", "integer")), ["table", "column"]),
            Tool("lint", "List indexed parse, dependency, and SQL quality diagnostics.", Properties(("limit", "integer")), []),
            Tool("query_builder", "Build a read-only, parse-validated SELECT preview for an indexed table or view.", Properties(("table", "string"), ("join", "boolean")), ["table"]),
            Tool("explain", "Explain a supported sqldeps diagnostic rule and its suggested direction.", Properties(("rule", "string")), ["rule"]),
            Tool("analyze", "Analyze unsaved SQL with ScriptDom and active index metadata.", Properties(("text", "string"), ("source_file", "string")), ["text"]),
            Tool("code_actions", "Preview safe LSP code actions for unsaved SQL without writing files.", Properties(("text", "string"), ("source_file", "string")), ["text"]),
            Tool("refactor_candidates", "List evidence-backed, parse-validated refactor previews without writing files.", Properties(("text", "string"), ("source_file", "string")), ["text"]),
            Tool("parameterize_dynamic_sql", "Build a safe sp_executesql argument preview without writing files.", Properties(("expression", "string"), ("declarations", "string")), ["expression", "declarations"]),
            Tool("test_skeleton", "Build a read-only EXEC test skeleton for an indexed procedure.", Properties(("procedure", "string")), ["procedure"]),
            Tool("drift", "Compare the indexed source folder with its stored file hashes.", Properties(("limit", "integer")), []),
        ];
    }

    private static object Tool(string name, string description, object properties, string[] required)
    {
        return new
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
    }

    private static object Properties(params (string Name, string Type)[] fields)
    {
        var properties = fields.ToDictionary(
            field => field.Name,
            field => (object)new
            {
                type = field.Type,
                minimum = field.Type == "integer" ? 1 : (int?)null,
            }
        );

        return properties;
    }
}
