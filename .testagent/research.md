# Test research

## Scope

The repository has a shell integration test (`test.sh`) but no C# test project. Static pairing found
14 source files and zero paired test files. The first unit-test slice targets pure, high-risk logic that
the shell test reaches only indirectly:

- `BatchSplitter`: `GO` boundaries, empty batches and source-line preservation.
- `SqlFileReader`: BOM decoding and deterministic SHA-256 hashes.
- `ReachabilityBuilder`: shortest paths, cycle reporting and ignored pseudo-objects.
- `IndexDatabase`: hash-based freshness reporting against an actual temporary SQLite index.

## Conventions discovered

- Production targets `net10.0`, uses nullable reference types and file-scoped namespaces.
- There is no test framework or solution file yet.
- Existing test names explain the expected behavior; tests run locally without services or network calls.

## Acceptance checklist

- Add a separate SDK-style xUnit project under `tests/sqldeps.Tests`.
- Assert concrete results for each listed behavior, including a boundary or negative case.
- Run the narrow test project and the existing `test.sh` integration suite.
- Preserve the existing command-line behavior.
