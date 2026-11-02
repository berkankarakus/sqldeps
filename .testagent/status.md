# Test status

## Validation

- `bash -n test.sh tests/smoke/*.sh` — passed.
- `./test.sh` — passed with `sqldeps: ok`.
- `dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj --no-restore -v minimal` — 24 passed.
- `git diff --check` — passed.

## Review

The existing LSP smoke suite was reviewed using mutation-style boundaries around
quick-fix confidence gating, document-range validation, parse-after-edit
validation, and preview-only output. The new corpus covers these as observable
JSON-RPC contracts:

- high-confidence typed-literal and NULL comparison edits;
- low-confidence replacement suppression with line suppression retained;
- out-of-document and parse-invalid replacement rejection;
- preview `edit` output without an executable `command`;
- representative DML, conversion, INSERT EXEC type, and nullability true/false
  positive boundaries.

The full repository-wide FP/FN acceptance criteria remain open for diagnostic
rules without a dedicated true-positive and negative fixture. Those are not
marked complete in `TODO.md`.
