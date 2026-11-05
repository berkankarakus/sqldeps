# Contributing

`sqldeps` extracts dependency and language-server information from SQL Server T-SQL scripts. Contributions
that improve a real SQL example or editor workflow are welcome.

## Before you start

```sh
git clone <repo-url>
cd sqldeps
dotnet restore
./test.sh
```

For faster feedback during development, use a targeted suite such as `./test.sh unit`, `./test.sh index`,
`./test.sh cli`, `./test.sh lsp`, `./test.sh quick-fix`, or `./test.sh fp-fn`. Before pushing, run
`./test.sh`; it validates fixture indexing, SQLite queries, CLI behavior, and the LSP/FP-FN JSON-RPC flows.

## Adding a change

- T-SQL parsing and dependency behavior live under `Indexing/` and `Domain/`.
- Persistent index and query behavior live under `Storage/` and `Cli/`.
- Editor behavior lives under `Lsp/`; Neovim configuration is maintained outside this repository.
- Before adding behavior, look for an existing helper or similar flow. Extend it instead of copying the same
  logic into a new path.
- Extract complex conditions into named methods and keep control flow easy to read.
- For new SQL behavior, add a fixture or focused test under `fixtures/` when possible. Never commit customer
  scripts, connection details, credentials, or other sensitive data.

## LSP changes

The LSP speaks JSON-RPC over stdio. When changing diagnostics, completion, or code actions, consider open
documents, workspace boundaries, and parse-error scenarios together. Client-specific Neovim Lua files do not
belong in this repository.

## Commits and pull requests

Each commit should describe one logical change and use a short, imperative message, for example
`feat: add column quick fix` or `refactor: split dependency collector`. A pull request description should
include:

- what changed and why,
- which commands or tests were run,
- any user-visible CLI or LSP behavior changes, and
- known limitations.

Do not combine large unrelated refactors in one pull request.

## Security

Do not disclose a vulnerability, real credential, or sensitive SQL example in a public issue. Contact the
maintainers through a private channel first. Include the affected version, reproduction steps, and, when
possible, a safe remediation proposal.
