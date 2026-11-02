# Test and implementation plan

## Phase 1 — quick-fix contract

- Extend `test.sh` with a low-confidence replacement case and assert suppression
  remains while text replacement is absent.
- Add explicit assertions for quick-fix `kind`, `data.source`,
  `data.confidence`, edit range, and `newText` for typed-literal, SELECT-star,
  WHERE-template, and similar-column actions.
- Add a parse-regression case whose candidate edit would make the batch invalid;
  assert no replacement action is returned.

## Phase 2 — FP/FN corpus

- Group fixtures by diagnostic family: conversion, nullability, DML filters,
  result-set types, metadata scope, and quick-fix safety.
- For each group, assert both the expected rule/range/confidence and the
  absence of the diagnostic for its nearest known false-positive boundary.
- Update TODO only for criteria backed by named regression assertions; retain
  explicitly unsupported cases as documented FN decisions.

## Phase 3 — safe code generation stage 1

- Keep generated changes as LSP preview edits; do not write files directly.
- Centralize the stage-1 contract in the existing `QuickFix` path: confidence
  threshold before generation, in-memory reparse after generation, and strict
  range/diff checks.
- Add a safe single-edit INSERT-column quick fix only if its target and source
  metadata are exact; otherwise leave the diagnostic explanation-only.
- Add regression coverage for the generated edit and all rejection guards.

## Validation

- Run `./test.sh`.
- Run `dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj --no-restore -v minimal`.
- Run `git diff --check` and verify the tree is clean after the requested commit.
