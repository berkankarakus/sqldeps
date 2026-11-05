# sqldeps DBeaver plugin

This directory contains the standalone Eclipse/OSGi plugin that connects the existing `sqldeps lsp`
server to the DBeaver SQL Editor. The SQL analysis engine and LSP server are not reimplemented here.

## Current scope

- Uses DBeaver's public `org.jkiss.dbeaver.sql.editorAddIns` extension point.
- Starts an `sqldeps lsp` process over stdio for SQL Editor sessions.
- Starts an LSP bridge for DBeaver procedure/view source tabs opened as `SQLEditorNested`, so Source tabs
  in Database Object Editor are analyzed as well.
- Uses LSP4J for `Content-Length` JSON-RPC framing. Server stdout is reserved for the protocol and stderr is
  forwarded separately to the DBeaver log.
- Implements `didOpen`, debounced full-text `didChange`, pull `textDocument/diagnostic`, and `didClose`.
- Converts UTF-16 LSP positions into DBeaver annotation-model offsets.
- The `Analyze SQL` command requests standard `textDocument/diagnostic` results.
- `Analyze SQL` and `Show sqldeps Diagnostics` work in both SQL Editor and nested Source editors.
- Diagnostics use DBeaver's standard annotation and hover flow; the command reveals the first finding and
  next/previous commands navigate between findings.
- Sessions start without an index by default.
- `Select sqldeps Index...` lists registered SQLite indexes by name and path and sends the selected path only
  to the active SQL Editor LSP session. If no registered index exists, it falls back to a file picker.
  `Use Automatic sqldeps Index` restores workspace matching and active-index fallback behavior.
- If the server advertises `codeActionProvider`, `Show All sqldeps Code Actions...` applies safe,
  active-document-only WorkspaceEdits after user confirmation.
- Command-based code actions are supported through a whitelist. The `Preview SELECT` and index recommendation
  results are obtained via `workspace/executeCommand`, shown to the user, and never written to disk.
- The server currently targets T-SQL/SQL Server. The DBeaver dialect is observed only as local context and
  is not sent because the server does not expose a standard dialect-configuration flow. No custom
  configuration method is invented, and connection details or passwords are never sent.

## Trying it in DBeaver

On macOS, run the following command from the repository root to automate most of the installation:

```bash
./dbeaver-plugin/install-local.sh
```

The script uses DBeaver's Java 21 runtime to build the bundle, downloads LSP4J 0.24.0 and the Eclipse
compiler from Maven Central, creates a local p2 repository, and installs it into DBeaver's existing
`DefaultProfile` with the Eclipse p2 director. If DBeaver is installed elsewhere:

```bash
DBEAVER_HOME=/absolute/path/to/DBeaver.app/Contents/Eclipse \
  ./dbeaver-plugin/install-local.sh
```

After installation, start DBeaver once with `-clean` and the path to the sqldeps executable:

```bash
/Applications/DBeaver.app/Contents/MacOS/dbeaver \
  -clean \
  -vmargs \
  -Dsqldeps.lsp.path=/Users/berkan/.local/bin/sqldeps
```

Use `./dbeaver-plugin/build-local.sh` to build only the bundle. The generated JAR and local p2 repository
are written to a temporary build directory and are not added to the repository. DBeaver must be closed
during installation.

The dependency-free Java adapter smoke tests require a Java 21 JDK:

```bash
./test.sh dbeaver-java
```

This suite covers document state, URI generation, index selection, debounce, and diagnostic request
cancellation. DBeaver's bundled JRE may be enough to run the plugin, but `java` and `javac` must also be
available on the system PATH to compile the tests.

To validate document synchronization against the Eclipse/JFace classes from an installed DBeaver:

```bash
./test.sh dbeaver-runtime
```

This smoke suite also validates restart, closing the previous connection, and idempotent shutdown through
the LSP document lifecycle seam with a fake connection.

To validate the LSP4J initialize, `didOpen`, diagnostics, and shutdown flow against a real `sqldeps lsp`
process:

```bash
./test.sh dbeaver-lsp-runtime
```

To measure the effect of the shared process pool under editor load:

```bash
./test.sh dbeaver-baseline
```

The output reports startup, diagnostics, active process, thread, and RSS values for 1/5/20 editors. If
resource measurement is unavailable, the thread/RSS fields remain `unavailable`. Set
`SQLDEPS_DBEAVER_PERF_SHARED_PROCESS=false` to compare the previous independent-process behavior.

1. Add a Java/JDK and LSP4J 0.24.x bundles matching DBeaver's Eclipse version to the PDE target platform.
2. Open this directory as an Eclipse PDE plug-in project and build the `org.sqldeps.dbeaver` bundle against
   a target platform containing DBeaver's `org.jkiss.dbeaver.ui.editors.sql` bundle.
3. Ensure `sqldeps` is on DBeaver's PATH. To use another executable, pass this JVM property when starting
   DBeaver:

   ```text
   -Dsqldeps.lsp.path=/absolute/path/to/sqldeps
   ```

   The plugin starts this executable with the `lsp` argument.
4. Deploy the `org.sqldeps.dbeaver` bundle to the DBeaver installation with PDE, or run it from an Eclipse
   Application launch configuration using the DBeaver product.
5. Open a SQL Editor and change SQL text. Diagnostics should appear as annotations. Use
   `sqldeps > Analyze SQL` for a manual request.

## Known limitations

- This first slice does not produce a general p2 update site; feature and repository projects will be added
  after local DBeaver validation is complete.
- DBeaver and LSP4J bundle versions must be validated against the target platform. The installed DBeaver
  includes a Java 21.0.8 JRE, but LSP4J bundles are not included and must be added to the PDE target.
