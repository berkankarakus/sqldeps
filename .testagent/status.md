# Test status

## Verification

- `dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj -c Release -nologo --artifacts-path /private/tmp/sqldeps-artifacts`
  passed: 6 tests, 0 failures.
- `./test.sh` passed.

## Assertion review

The unit tests use direct, mutation-sensitive assertions: exact batch text and
source lines, decoded text and SHA-256 properties, exact reachability paths and
cycle depth, and every field of the changed-file status. They use no sleeps,
network calls, skipped tests, or shared persistent state. The SQLite test owns a
unique temporary directory and removes it through `IDisposable`.

## Remaining coverage

This is a focused baseline rather than full coverage. Command-line parsing and
rendering, cache/index orchestration, SQL dependency collection, object-name
normalization, and filesystem indexing remain covered chiefly by `test.sh`.
A larger follow-up should add fixture-driven tests for those components and a
separate, opt-in corpus test with real-world SQL files. The latter must not be a
mandatory network download during normal test runs.
