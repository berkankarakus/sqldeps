# Test research

## Scope

This pass covers the three requested TODO areas:

1. quick-fix regression evidence;
2. FP/FN acceptance criteria for diagnostics;
3. safe code-generation stage 1, which currently consists of single-edit LSP
   actions rather than direct file writes.

## Existing conventions

- `tests/sqldeps.Tests` is an xUnit test project targeting `net10.0`.
- `IndexDatabaseTests` covers indexer/storage behavior with temporary folders.
- LSP behavior is exercised end-to-end by `test.sh`, which sends JSON-RPC to
  `dotnet run -c Release --no-build -- lsp` and asserts diagnostic/action JSON.
- The LSP quick-fix implementation is split between
  `Lsp/LanguageServer.CodeActions.cs` and `Lsp/LanguageServer.cs`.
- Quick fixes are generated through `QuickFix`, which validates document range,
  applies the edit in memory, and rejects edits that add parse problems.
- Low-confidence diagnostics are filtered before replacement actions are built;
  suppression and stale-index reindex actions remain available.

## Static source pairing

The required Roslyn pairing scan found 31 source files and 5 test files, with 22
unpaired source files. The relevant unpaired files are:

- `Lsp/LanguageServer.CodeActions.cs`
- `Lsp/LanguageServer.cs`
- `Lsp/QueryColumnValidator.cs`
- `Lsp/ProcedureParameterValidator.cs`
- `Indexing/DependencyCollector.cs`

The existing LSP smoke corpus is the established integration seam for these
private request handlers, so this pass extends that seam instead of exposing
implementation details solely to unit tests.

## Acceptance checklist

- Every current replacement quick fix has a regression assertion for title,
  range, new text, and confidence/source metadata where applicable.
- A malformed/out-of-range diagnostic cannot produce a replacement edit.
- A low-confidence diagnostic cannot produce a text replacement, while its
  suppression action remains available.
- A valid edit that would introduce a parse error is rejected.
- A valid edit is limited to the diagnostic/insertion range.
- Representative diagnostic rules have explicit true-positive and negative
  fixtures, with rule, range, and confidence asserted.
- FP/FN decisions for unsupported or ambiguous cases are recorded in TODO and
  guarded by regression fixtures.
- Stage 1 code generation remains preview-only: it returns an LSP workspace edit,
  applies no direct filesystem write, enforces confidence/range/parse guards,
  and records the generated edit contract in regression tests.
