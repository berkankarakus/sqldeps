# Test plan

1. Create the `net10.0` xUnit project with a project reference to `SqlDeps.csproj`.
2. Add unit tests for batch splitting, encodings/hashes, reachability/cycles and index freshness.
3. Restore packages and run `dotnet test tests/sqldeps.Tests/sqldeps.Tests.csproj`.
4. Run `./test.sh` to retain end-to-end coverage of parsing and CLI commands.
5. Review assertions for direct, mutation-sensitive expected values and record the outcome in `status.md`.
