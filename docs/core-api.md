# Core API

`SqlDeps.Core` is the adapter-facing .NET library used by the CLI, LSP, MCP, and DBeaver integrations.
It exposes read-only analysis and preview contracts; it does not connect to SQL Server or write source files.

## Contracts

| API | Responsibility |
| --- | --- |
| `IndexQueryService` | Parameterized reads from the selected SQLite index and metadata snapshots. |
| `IndexAnalysisMetadata` | Immutable schema, key, foreign-key, index, object, and parameter input for document analysis. |
| `SqlQueryBuilder` | Parse-validated `SELECT` and relationship previews. |
| `SqlFormatter` | Non-writing T-SQL formatting previews with parse-error results. |
| `SqlTestSkeletonBuilder` | Non-writing procedure execution skeletons with nullable arguments. |
| `RefactorCandidateProjector` | Converts LSP action payloads into typed, non-writing candidates. |
| `RefactorImpactResolver` | Resolves static dependency impact and marks dynamic edges as partial. |
| `RefactorCandidate` and related records | Shared candidate, edit, evidence, and impact data contracts. |

## Safety boundary

- Index reads use allow-listed operations and parameters; arbitrary SQL is not accepted by the query service.
- Generated SQL is parsed before it is returned as a preview.
- Refactor candidates must carry a supported confidence, applicability, evidence, and `writes_file=false`.
- Stale indexes, incomplete dependency impact, malformed edits, and multi-file edits are rejected by the safe
  advisor path.
- `SqlFormatter` returns the original text with errors when a batch cannot be parsed or a supported safe rewrite
  boundary is not met.

The XML documentation emitted by `Core/SqlDeps.Core.csproj` is the IDE/package reference for the same contract.
`CoreApiCompatibilityTests` protects the assembly, namespace, exported types, and factory method signatures.
