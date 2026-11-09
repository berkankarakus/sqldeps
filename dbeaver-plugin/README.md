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
- Implements `didOpen`, debounced UTF-16 range-based `didChange`, pull `textDocument/diagnostic`, and `didClose`.
- Converts UTF-16 LSP positions into DBeaver annotation-model offsets.
- The `Analyze SQL` command requests standard `textDocument/diagnostic` results.
- `Analyze SQL` and `Show sqldeps Diagnostics` work in both SQL Editor and nested Source editors.
- Diagnostics use DBeaver's standard annotation and hover flow; the command reveals the first finding and
  next/previous commands navigate between findings.
- Sessions use automatic workspace index selection by default; `Select sqldeps Index...` can override it for the
  active editor and `Use Automatic sqldeps Index` restores the workspace-driven choice.
- The `Reindex sqldeps Index` command rebuilds the active index through the LSP and refreshes the current editor
  session. Stale diagnostics point to reindexing, while missing automatic/manual indexes point to index selection.
- The core analysis, diagnostics, code-action, index-selection, and reindex commands are also available from the
  SQL Editor context menu under `sqldeps`; the main-menu entries remain available for keyboard and Quick Access use.
- The standard DBeaver SQL formatter command (`org.jkiss.dbeaver.ui.editors.text.content.format`) is handled by
  sqldeps while a SQL editor is active, so the editor's format shortcut uses the parse-validated sqldeps formatter.
- The status line reports LSP lifecycle states (`starting`, `analyzing`, `ready`, `restarting`, `reindexing`, and
  `failed`) so an index change or process failure is visible instead of appearing as a silent restart.
- `Select sqldeps Index...` lists registered SQLite indexes by name and path and sends the selected path only
  to the active SQL Editor LSP session. If no registered index exists, it falls back to a file picker.
  `Use Automatic sqldeps Index` restores workspace matching and active-index fallback behavior.
- If the server advertises `codeActionProvider`, `Show All sqldeps Code Actions...` applies safe,
  active-document-only WorkspaceEdits after user confirmation.
- Parse-validated rewrites identify themselves with `generation=parse-validated-rewrite` and
  `writesFile=false`; review-only suggestions use `generation=explanation-only` and can carry documentation
  references in the action metadata. The action menu shows the first evidence title and source count, while
  the review preview shows the full guidance and source URLs.
- Command-based code actions are supported through a whitelist. The `Preview SELECT` and index recommendation
  results are obtained via `workspace/executeCommand`, shown to the user, and never written to disk.
- Diagnostic explanations are available as read-only preview actions for catalogued rules; semantic-risk diagnostics
  retain their explanation-only review behavior instead of speculative edits. DBeaver shows the rule guidance and
  source references in the same preview dialog.
- Selecting an indexed object and opening code actions exposes a read-only repository context preview with uses,
  incoming references, and reverse impact. The result is displayed in the same command preview dialog and is never
  written to the editor.
- The server currently targets T-SQL/SQL Server. The DBeaver dialect is observed only as local context and
  is not sent because the server does not expose a standard dialect-configuration flow. No custom
  configuration method is invented, and connection details or passwords are never sent.

## Source structure

- `lsp/` owns the process boundary, JSON-RPC client, document snapshots, and UTF-16 conversion that are independent
  of editor widgets.
- `editor/index/` owns index discovery, persisted explicit selection, automatic fallback, and status formatting.
- `ui/` owns SQL Editor and nested Source Editor lifecycle, diagnostics, annotations, code actions, and scheduling.
- `commands/` contains thin Eclipse handlers; handlers delegate to the active editor bridge instead of owning LSP
  state or SQL analysis.
- `test/` contains dependency-free Java tests and runtime fixtures. The plugin does not duplicate the SQL analysis
  engine; all parsing and diagnostics remain in the `sqldeps lsp` process.

The package boundaries are intentionally stable before introducing more DBeaver-specific features. If a UI class
needs to understand JSON-RPC framing or process restart details, move that behavior into `lsp/` rather than adding
another cross-package helper.

### Recommended Java structure

Keep the current three-package layout while the adapter is still small enough to validate as one plug-in. The next
split should be driven by dependency direction rather than file count:

- keep index discovery and selection in `editor/index`; its persisted user preference and automatic fallback are
  already isolated from editor widgets;
- split diagnostics and annotations into `ui.diagnostics` when another editor surface shares that flow;
- keep `commands` as thin handlers and keep all process, JSON-RPC, and document-version logic in `lsp`;
- do not introduce a service or interface layer unless a second editor surface or a test double needs it.

This keeps the current build simple and gives future features a measurable extraction rule: a package moves only when
its dependencies are narrower than the package it leaves.

## Editor surfaces: verified behavior

DBeaver has two editor surfaces and they behave differently. The facts below were verified in DBeaver 26.2.x and are
pinned by `dbeaver-plugin/test/org/sqldeps/dbeaver/PluginDescriptorTest.java` and `tests/smoke/dbeaver-plugin.sh`.
Do not change them without checking both surfaces by hand.

| Topic | Regular SQL editor | Nested Source tab (table, view, procedure) |
| --- | --- | --- |
| Active editor seen by commands | `SQLEditor` | An outer entity editor; resolve the editor with `getAdapter(SQLEditorBase.class)` or `NestedSqlEditorLspBridge.active()` |
| Handler scope | Works | `activeWhen` scopes were not satisfied: Quick Fix failed with "Can't execute command" and index selection did nothing. No handler uses `activeWhen` or `enabledWhen` |
| Context menu | `plugin.xml` popup `org.jkiss.dbeaver.ui.editors.sql.SQLEditor.EditorContext?after=sql.extras` (`popup.any` does not reach it) | No `plugin.xml` popup id reaches it; `NestedEditorContextMenu` adds the submenu through `BaseTextEditor.addContextMenuContributor` |
| Toolbar | Side toolbar `...toolbar.side.top` with icon-only buttons | No SQL side toolbar and no sqldeps buttons; use the menu or `Cmd+Option+K` |
| Repository root | Resolved through the LSP command `sqldeps.repository-root` | Same; the plugin class loader has no SQLite JDBC driver, so reading the index file directly returns "unknown" |

Other verified pitfalls:

- `Window.getShell()` is `null` until `create()` has run. Create a dialog before attaching shell listeners.
- `java.util.prefs` is not written to disk until `flush()`; without it the index selection was lost on restart.
- The CLI and the plugin must be installed together (`./install.sh all`); `sqldeps --version` and the plugin log line
  `sqldeps LSP server sqldeps <version>` show whether they match.
- Run DBeaver with `-clean` once after installing the plugin.

Manual check after changing menus, handlers or index handling, on both surfaces: Quick Fix on a diagnostic, the sqldeps
context menu, **Select sqldeps Index...**, **Manage sqldeps Repository Suppressions...** and the impact command.

## Trying it in DBeaver

On macOS, the application bundle under `/Applications` is normally write-protected. Copy DBeaver to a
user-owned location first, then run the installer against that copy:

```bash
mkdir -p "$HOME/Applications"
ditto /Applications/DBeaver.app "$HOME/Applications/DBeaver-sqldeps.app"

DBEAVER_HOME="$HOME/Applications/DBeaver-sqldeps.app/Contents/Eclipse" \
  ./dbeaver-plugin/install-local.sh
```

`./install.sh dbeaver` is a shorter entry point for the same script. It uses
`$HOME/Applications/DBeaver-sqldeps.app` automatically when that copy exists and `DBEAVER_HOME` is unset,
and it refuses to run while DBeaver is open.

The script uses DBeaver's Java 21 runtime to build the bundle, downloads LSP4J 0.24.0 and the Eclipse
compiler from Maven Central, creates a local p2 repository, and installs it into DBeaver's existing
`DefaultProfile` with the Eclipse p2 director. The target DBeaver installation must be writable; the
installer fails early with a copy command when it detects a protected application bundle. If DBeaver is
already installed in a writable location:

```bash
find /Applications "$HOME/Applications" -maxdepth 3 -type d -path '*/Contents/Eclipse' -print

DBEAVER_HOME=/absolute/path/to/DBeaver.app/Contents/Eclipse \
  ./dbeaver-plugin/install-local.sh
```

If Maven Central is unavailable, pre-populate `SQLDEPS_DBEAVER_DEPS_DIR` with the three required JARs or
set `SQLDEPS_DBEAVER_MAVEN_BASE_URL` to a reachable mirror.

After installation, start the writable copy once with `-clean` and the path to the sqldeps executable:

```bash
"$HOME/Applications/DBeaver-sqldeps.app/Contents/MacOS/dbeaver" \
  -clean \
  -vmargs \
  -Dsqldeps.lsp.path=/Users/berkan/.local/bin/sqldeps
```

Experimental rewrite and code-generation actions are opt-in. Enable them only while reviewing their
previews from the `sqldeps > Experimental Code Actions` toggle. The preference is stored per DBeaver user
profile, never writes files automatically, and asks for confirmation before changing mode. Changing it
restarts active SQL Editor and nested Source Editor LSP sessions so the new mode applies immediately. For
a headless or first-start configuration, it can also be enabled with this JVM property:

```text
-Dsqldeps.lsp.experimentalSuggestions=true
```

Without this property the plugin keeps the server's default safe mode and does not request experimental
WorkspaceEdits.

The SQL Editor toolbar exposes the three most common sqldeps actions. Their default shortcuts are
`M1+M3+A` (Analyze SQL), `M1+M3+K` (Code Actions), and `M1+M3+L` (Diagnostics); Eclipse's
Quick Access can also find every sqldeps command by name.

Use `./dbeaver-plugin/build-local.sh` to build only the bundle. The generated JAR and local p2 repository
are written to a temporary build directory and are not added to the repository. DBeaver must be closed
during installation.

To build a reusable p2 update site without installing it into DBeaver, run:

```bash
./dbeaver-plugin/build-update-site.sh
```

The command publishes the plugin, LSP4J dependencies, and the `org.sqldeps.dbeaver.feature` metadata into
`$SQLDEPS_DBEAVER_UPDATE_SITE_DIR` or the temporary build directory. The resulting directory can be served
over HTTP or added to Eclipse/DBeaver as a local update-site location. The p2 publisher uses a temporary,
writable configuration area by default; set `SQLDEPS_DBEAVER_P2_CONFIGURATION_DIR` to provide another one.

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
   `sqldeps > Analyze SQL` or the SQL Editor context menu for a manual request.

## Known limitations

- DBeaver and LSP4J bundle versions must be validated against the target platform. The installed DBeaver
  includes a Java 21.0.8 JRE, but LSP4J bundles are not included and must be added to the PDE target.
