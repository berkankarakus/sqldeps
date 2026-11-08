# sqldeps

[![CI](https://github.com/berkankarakus/sqldeps/actions/workflows/ci.yml/badge.svg)](https://github.com/berkankarakus/sqldeps/actions/workflows/ci.yml)

See [CONTRIBUTING.md](CONTRIBUTING.md) for the contribution guide.

`sqldeps` reads `.sql` files with Microsoft's T-SQL parser (ScriptDom), records which objects use which
other objects and operations, and stores the result in a SQLite index. The same analysis powers the CLI,
the LSP server, and the MCP server.

```sh
sqldeps --help                                  # show command usage
sqldeps --version                               # show the installed version
./install.sh                                    # install the CLI to ~/.local/bin/sqldeps
./install.sh dbeaver                            # install the DBeaver plugin (DBeaver must be closed)
./install.sh all                                # CLI and DBeaver plugin
sqldeps index ~/Projects/sql-repo               # use ~/.local/share/sqldeps/sql-repo.sqlite
sqldeps index ~/Projects/sql-repo --db deps.sqlite
sqldeps index ~/Projects/sql-repo --default-schema app
sqldeps index ~/Projects/sql-repo --jobs 4      # process up to four files concurrently
sqldeps index ~/Projects/sql-repo --full        # ignore the incremental cache
sqldeps index ~/Projects/sql-repo --json        # emit a stable JSON summary
sqldeps status ~/Projects/sql-repo              # inspect index freshness and counts
sqldeps lint --fail-on warning                  # exit 1 for warning or error findings
sqldeps lint --sarif > sqldeps.sarif            # emit GitHub Code Scanning-compatible diagnostics
sqldeps lint --profile security --json          # show security-focused findings
```

## Indexing

- **Incremental indexing:** repeated runs process only changed, new, or deleted files. Each file's SHA-256
  digest is stored in the `files` table. The output reports how many unchanged files were reused. A schema
  or rules-version change invalidates the cache.
- **Parallel parsing:** files are parsed concurrently. `--jobs <n>` sets an explicit worker limit; the
  default is `min(CPU count, 4)`. The SQLite database is built in a temporary file and atomically replaces
  the previous database only after a successful run.
- **Interactive progress:** indexing, reachability, and SQLite stages are shown as a single progress bar in
  an interactive terminal. Progress is disabled for JSON, quiet, CI, and redirected output.
- **Encoding:** BOM-marked UTF-8, UTF-16, and UTF-32 are supported. Without a BOM, UTF-8 is attempted first;
  invalid input falls back to Windows-1254. BOM-less UTF-16 is not guessed.
- **Default schema:** `--default-schema` controls how unqualified names are normalized; the default is `dbo`.

## SQLite schema

| Table | Contents |
|---|---|
| `dependencies` | `source_object`, `target_object`, `operation`, `source_file`, `line` |
| `objects` | object name, kind, source file, and line |
| `parse_errors` | source file, line, and parser message |
| `files` | indexed path and SHA-256 digest |
| `meta` | index settings and metadata |
| `parameters` | procedure/function signatures and defaults |
| `column_usage` | safely resolved `SELECT`, `INSERT`, and `UPDATE` column usage |
| `table_columns` | table/view columns and LSP completion metadata |
| `reach` | indirect dependencies, shortest depth, and path (up to 12 steps) |
| `cycles` | direct or indirect cycles and shortest return path |
| `readers`, `writers`, `calls` | filtered views over `dependencies` |
| `impact` | reverse reachability: objects affected by a change |
| `undefined_objects` | referenced objects without definitions in the index |
| `unused_objects` | objects unused by other indexed objects, excluding triggers |
| `most_written` | tables written by the largest number of distinct objects |
| `object_degree` | incoming and outgoing dependency counts |
| `duplicate_definitions` | objects defined in multiple locations, including later `ALTER` definitions |

Names are normalized as `schema.name` (or `database.schema.name`) and compared case-insensitively. Every
use remains a separate row in `dependencies`; use `SELECT DISTINCT` when you need unique pairs.

Operations include `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, `EXEC`, `CALL`, `ON`, and
`EXEC_DYNAMIC`. Literal `EXEC` and `sp_executesql` strings are analyzed separately and marked with
`via_dynamic = 1`.

## CLI queries and index selection

After indexing, routine queries read SQLite directly. Without `--db`, commands use the default index for the
current directory. You can select an index once and use it from any directory:

```sh
sqldeps use sql-repo                            # select a named local index
sqldeps use /absolute/path/deps.sqlite
sqldeps use sql-repo --json
sqldeps prompt                                  # short active-index name for a shell prompt
sqldeps prompt --json
sqldeps reindex                                 # rebuild the active index from its recorded source root
sqldeps indexes                                 # list local indexes and source roots
sqldeps doctor                                  # check source root and freshness
sqldeps drift                                   # list file differences between source and index
sqldeps report                                  # combine status, drift, and lint summaries
sqldeps graph --format json                     # export direct dependency graph as JSON
sqldeps graph --format dot                      # export direct dependency graph for Graphviz
sqldeps unuse                                   # clear active-index selection without deleting the file
sqldeps drop sql-repo                           # prompt before deleting an index
sqldeps drop sql-repo --yes                     # skip confirmation in automation
sqldeps source UpdateProducts                   # show definition file and line
sqldeps objects --limit 20                      # list indexed objects
sqldeps find Products                           # case-insensitive object search
sqldeps table-columns Products                  # show columns available to LSP completion
sqldeps query-builder Products                  # generate a safe single-source SELECT preview
sqldeps query-builder OrderLines --join
sqldeps test-skeleton RunReport                 # generate a safe procedure EXEC preview
sqldeps refactor-candidates query.sql --json    # list safe typed refactor previews
sqldeps explain select-star                     # explain a diagnostic rule
sqldeps mcp --db deps.sqlite                    # serve MCP tools over stdio
```

`--db` overrides the active index. `status <folder>` compares that folder with its own default index rather
than the active selection. `drop` asks for `[y/N]` by default and prints the deleted index name and path on
success. Index metadata records the source root, so argument-less `reindex` can rebuild it. Old indexes that
lack this metadata require `sqldeps reindex <folder> --db <file>`.

The index format is intentionally rebuilt, not migrated, when rules or schema metadata change. A temporary
SQLite file is written and atomically moved into place after success.

`prompt` prints only the active index's extensionless name and stays silent when no index is selected. For an
optional zsh prompt segment:

```zsh
setopt prompt_subst
sqldeps_prompt_segment() {
  local index
  index="$(command sqldeps prompt 2>/dev/null)"
  [[ -n "$index" ]] && print -n " [sqldeps: $index]"
}
PROMPT='%n@%m %1~$(sqldeps_prompt_segment) %# '
```

Interactive query output preserves colors through `less` when it exceeds the terminal height. Use
`--no-pager` for scripts or plain output. Grouped output is the default; `--table` restores the flat table
view, `--json` emits machine-readable arrays, `--limit` limits tabular results, and `--no-color` disables
colorization.

```sh
sqldeps who-writes dbo.Products --db deps.sqlite
sqldeps who-updates dbo.Products price --db deps.sqlite
sqldeps who-reads dbo.OrderSummary --db deps.sqlite
sqldeps deps dbo.UpdateProducts --db deps.sqlite
sqldeps columns dbo.UpdateProducts --db deps.sqlite
sqldeps table-columns dbo.Products --db deps.sqlite
sqldeps query-builder dbo.Products --db deps.sqlite --json
sqldeps impact dbo.Products --db deps.sqlite
sqldeps duplicates --db deps.sqlite
sqldeps errors --db deps.sqlite
sqldeps lint --db deps.sqlite --fail-on warning
```

`query-builder` quotes SQL Server identifiers, uses a `t` alias, and never writes files. It refuses to produce
a preview when metadata is missing or ambiguous. `table-columns` preserves primary-key and foreign-key
metadata next to column names.

`explain` includes evidence references when a rule has a documented SQL Server behavior behind it. These
references support review and experimental suggestions; they do not turn a static finding into an execution-plan
or production-performance guarantee.

## MCP

`sqldeps mcp` is a supported read-only integration surface. It serves the active or `--db` SQLite index as
MCP tools over stdio. Requests are single-line JSON-RPC messages and responses are single-line JSON. Arbitrary
SQL is not accepted; tools use the parameterized queries exposed by `IndexQueryService`.

Available tools include `find_objects`, `objects`, `source`, `table_columns`, `deps`, `impact`, `who_writes`,
`who_reads`, `who_updates`, `lint`, `query_builder`, `explain`, `analyze`, `parameterize_dynamic_sql`,
`test_skeleton`, `code_actions`, `refactor_candidates`, and `drift`. Listing/query tools expose both JSON text in `content` and
structured rows in `structuredContent.rows`. `analyze` checks unsaved SQL against the active index.
`code_actions` returns the same safe LSP action previews without applying edits. The CLI
`refactor-candidates` command and MCP `refactor_candidates` tool consume the same typed Core candidate contract;
LSP quick fixes expose it under `data.refactorCandidate`. Dynamic SQL parameterization
and procedure test skeletons return previews only and never write files. `refactor_candidates` projects only
parse-validated, evidence-backed editable actions with `writes_file: false`; stale, explanation-only, suppression,
or missing-confidence actions are excluded. Each candidate includes indexed `affected_dependencies` and a `graph`
summary with direct dependency count, affected downstream object count, and maximum known impact depth. Candidates
are ordered by confidence and then by graph impact. The impact status is `partial` when the analyzed document
contains unresolved dynamic SQL dependencies.

MCP also exposes each diagnostic explanation as a read-only resource through `resources/list` and
`resources/read` using URIs such as `sqldeps://diagnostic/select-star`. Resource contents include the rule,
summary, guidance, default severity, and evidence links. LSP diagnostics expose the same evidence under
`data.evidence` for clients that render diagnostic details.

The server negotiates MCP protocol version `2024-11-05`. Unsupported versions return JSON-RPC error `-32602`.
Malformed JSON returns `-32700`, invalid envelopes return `-32600`, unknown methods return `-32601`, invalid
tool names or arguments return `-32602`, and unexpected server failures return `-32603`. Successful tool calls
use a JSON-RPC result with `content` and, where applicable, `structuredContent`; previews always declare
`writes_file: false`. Request lines are limited to 32 MiB, while analysis and parameterization inputs have
their own lower limits.

CLI exit codes are stable:

| Code | Meaning |
|---:|---|
| `0` | Success; lint did not cross its configured threshold |
| `1` | Index is stale or `lint --fail-on` crossed its threshold |
| `2` | Usage error, unknown option/command, or inaccessible index |

## Linting and diagnostics

`lint` reports parser errors, unresolved dynamic SQL, duplicate definitions, unused objects, cross-database
references, `SELECT *`, `NOLOCK`/`READUNCOMMITTED`, unfiltered `SELECT`, `MERGE`, and `UPDATE`/`DELETE`
without a `WHERE`. Findings carry `error`, `warning`, or `info` severity and a source location when one is
known. `lint --json` shares `source`, `confidence`, and `stale` fields with LSP diagnostics.
`lint --sarif` emits SARIF 2.1.0 with rule IDs, severity levels, source locations, and the same diagnostic
metadata. The CI workflow uploads this report together with the performance baseline as a build artifact and
publishes SARIF to code scanning for non-pull-request runs.
Profiles currently include `default` (all rules), `security` (unsafe data access and broad-write rules), and
`performance` (access-path and query-shape rules). Profile filtering happens before `--fail-on` evaluation.

Common diagnostic codes include `unknown-column`, `ambiguous-column`, `unknown-procedure`,
`unknown-function`, `unknown-procedure-parameter`, `missing-procedure-parameter`, `ambiguous-table`,
`cte-alias-shadowing`, `select-star`, `nolock`, `merge`, `leading-wildcard-like`, `non-sargable-function`,
`implicit-filter-conversion`, `unfiltered-select`, `update-without-where`, `parse-error`, and `dynamic-sql`.

To suppress a known finding for the next line:

```sql
-- sqldeps: disable-next-line=merge
MERGE dbo.Target AS target USING dbo.Source AS source ON target.Id = source.Id
WHEN MATCHED THEN UPDATE SET Value = source.Value;
```

Multiple rules can be comma-separated, or `all` can be used. Suppression applies to both CLI lint and LSP
diagnostics. LSP clients that support code actions also receive a **Disable sqldeps ... for this file** quick fix.

For broader scopes, use a file directive anywhere in the SQL file:

```sql
-- sqldeps: disable-file=select-star,unfiltered-select
```

Or add a `.sqldeps` file at the indexed repository root:

```text
sqldeps: disable-repo=legacy-rule
```

Repository, file, and next-line suppressions are additive. A narrower scope cannot re-enable a rule disabled
by a broader scope; `all` disables every lint rule at that scope.

## LSP

`sqldeps lsp` is a supported T-SQL integration surface. An LSP client such as Neovim starts it over stdio;
it is not a terminal command. It publishes parse and lint diagnostics for unsaved SQL, continues using a valid
prefix when possible, and supports workspace/document symbols, object and column-level go-to-definition and
references, call hierarchy, hover, completion, and safe code actions. Column navigation follows resolved table
metadata and indexed column usage; ambiguous or unresolved columns are left untouched. Call hierarchy follows
indexed `EXEC` and `CALL` relationships; unresolved dynamic SQL is not included. Rename and formatting are
intentionally out of scope.

For an unfiltered `UPDATE` or `DELETE`, sqldeps can generate a primary-key predicate when every
primary-key column has a declared parameter with the same name. When names differ, add an explicit
mapping immediately above the statement:

```sql
DECLARE @productId int;
-- sqldeps: key-parameter=@productId -> col_a
UPDATE dbo.Products SET title = @title;
```

Mappings are used only when every primary-key column is mapped to one declared parameter. Incomplete,
ambiguous, or stale cases keep the non-destructive `WHERE 1 = 0` template instead.

Documents larger than 16 MiB use limited analysis: complete batches within the budget are analyzed and a
low-confidence `partial-analysis` diagnostic explains that the result is incomplete. Partial results are never
reported as clean. The JSON-RPC transport accepts up to 64 MiB so this status can be delivered for large editor
documents without removing the indexing limits.

Completion uses indexed tables, views, procedures, functions, CTE columns, derived subqueries, table
variables, and temporary tables. Safe code actions can add missing procedure arguments, complete supported
`INSERT` values with typed `NULL` casts, generate a read-only SELECT preview, and explain diagnostics. Dynamic
SQL suggestions are preview-only and reject unresolved object names or types. Text-edit actions identify
themselves as `parse-validated-rewrite` with `writesFile=false` in action metadata, so adapters can preview or
gate them without treating them as arbitrary file generation.

Selecting an indexed object in a code-action request exposes a read-only repository context preview. It summarizes
the object's outgoing uses, incoming references, and reverse impact. Object hover includes the same relationship
counts without requiring a command.

The server supports pull diagnostics through `textDocument/diagnostic` and `workspace/diagnostic`. If the
client advertises pull diagnostics during `initialize`, the server does not also send duplicate
`publishDiagnostics` notifications for the same document.

### Live database clients

Use a separate client such as Vim-Dadbod for live queries and database sessions:

- `sqldeps lsp`: static dependency analysis, linting, completion, and safe previews from source files.
- Vim-Dadbod: live connections, query execution, and database sessions.

`sqldeps` does not store credentials, open live database connections, or merge live query results into index
metadata.

## Architecture

The CLI and LSP use the same data path:

```text
CLI / LSP document -> ScriptDom parser -> DependencyCollector -> models -> SQLite / LSP response
```

- `Program.cs` dispatches commands and terminal output.
- `Indexing/SqlFolderIndexer.cs` discovers SQL files, reuses unchanged files through `IndexCache`, and parses
  files in parallel.
- `Indexing/DependencyCollector*.cs` walks ScriptDom trees for calls, writes, definitions, and dynamic SQL.
- `Storage/` owns the SQLite schema, index writer, and query/report views.
- `Core/IndexQueryService.cs` provides the shared parameterized query boundary for CLI and MCP.
- `Mcp/McpServer.cs` implements the read-only stdio MCP server.
- `Analysis/DocumentAnalysis.cs` analyzes unsaved documents batch by batch, keeps valid prefixes after parse
  errors, and exposes limited/partial analysis for oversized documents. `QueryColumnValidator` and
  `ProcedureParameterValidator` share this analysis between LSP and MCP.
- `Lsp/LanguageServer.cs` routes JSON-RPC requests. Completion and code actions live in their focused partial
  files; navigation and repository context combine workspace metadata with the open-document snapshot.
- `dbeaver-plugin/` contains the independent Eclipse/OSGi adapter. It shares the executable and LSP protocol,
  but owns DBeaver editor synchronization and lifecycle management.

LSP and MCP intentionally remain in the same executable. A separate project becomes worthwhile only when
independent distribution, versioning, or isolation is required.

### Core library API boundary

`Core/SqlDeps.Core.csproj` is the shared class library used by the CLI, LSP, and MCP adapters. Its documented
public contract is the `SqlDeps.Core` namespace:

- `IndexQueryService` and `IndexAnalysisMetadata` provide read-only indexed metadata access.
- `RefactorCandidate*` types, `RefactorCandidateProjector`, and `RefactorImpactResolver` provide the shared,
  evidence-backed refactor contract.
- `SqlQueryBuilder`, `SqlFormatter`, and `SqlTestSkeletonBuilder` provide parse-validated, non-writing previews.

The `SqlDeps` domain records are shared data contracts consumed by these APIs. ScriptDom visitors, parser helpers,
SQLite schema/row mappers, and storage implementation details remain internal implementation seams; adapters should
depend on the Core contracts rather than their underlying parser or SQLite types. The Core project emits XML
documentation for IDE and package consumers, while `CoreApiCompatibilityTests` protects the namespace and
assembly boundary.

The complete adapter-facing contract is documented in [`docs/core-api.md`](docs/core-api.md).

## Scope and limitations

- Variable or otherwise unresolvable dynamic SQL (`EXEC (@sql)`) is reported as `EXEC_DYNAMIC` without guessed
  dependencies. Literal strings and constant concatenations are analyzed.
- The tool does not connect to a runtime database, inspect live metadata, or store credentials.
- LSP code actions return previews or user-confirmed edits; the server does not write files automatically.
- `unused_objects` describes the indexed SQL graph only; callers from application code or schedulers are not
  visible.
- Reachability is limited to 12 steps.
- Temporary tables, CTE names, table variables, and trigger pseudo-tables are document-local analysis inputs;
  they are not persisted as ordinary graph objects. LSP completion and validation can still use their columns
  while the document is open.
- `column_usage` intentionally covers only safely resolved simple statements.
- The parser targets SQL Server 2022 (`TSql160`). Unreadable batches are recorded in `parse_errors` while
  later batches continue indexing.
- Indexed files larger than 64 MiB and batches larger than 16 MiB are rejected to bound resource usage. Open LSP
  documents above 16 MiB use limited analysis and report `partial-analysis` instead of pretending to be clean.
- Other SQL dialects are not supported yet; provider-specific parser and diagnostic work is planned separately.
- The DBeaver adapter is locally installable and a reusable p2 update site can be built with
  `dbeaver-plugin/build-update-site.sh`; publishing it publicly is intentionally left to deployment tooling.

## Tests

```sh
./test.sh                         # all smoke suites
./test.sh unit                    # .NET unit tests
./test.sh index                   # indexing scenarios
./test.sh cli                     # CLI scenarios
./test.sh lsp                     # LSP smoke suite
./test.sh mcp                     # MCP smoke suite
./test.sh quick-fix               # quick-fix acceptance matrix
./test.sh quick-fix --no-build    # reuse the existing Release apphost during iteration
./test.sh perf                    # indexing and repeated LSP baselines
./test.sh fp-fn                   # false-positive/false-negative corpus
./test.sh dbeaver-java            # dependency-free Java adapter tests
./test.sh dbeaver-runtime         # Eclipse/JFace adapter tests
./test.sh dbeaver-lsp-runtime     # real sqldeps LSP process test
./test.sh dbeaver-baseline        # DBeaver process-pool performance baseline
```

Run `./test.sh` (or `./test.sh all`) before pushing changes. The suite covers fixture indexing, CLI queries,
LSP/MCP JSON-RPC flows, FP/FN assertions, and the adapter smoke tests that are available in the local
environment. Use `--no-build` only after a successful build when iterating without source changes that affect
the compiled apphost.
