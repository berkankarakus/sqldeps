/*
 * Copyright (c) 2026.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.example.dbeaver.ui;

import java.io.PrintWriter;

import org.jkiss.dbeaver.ui.editors.sql.SQLEditor;
import org.jkiss.dbeaver.ui.editors.sql.addins.SQLEditorAddIn;

/** Minimal SQL Editor lifecycle hook for an LSP-backed DBeaver plugin. */
public final class TemplateSqlEditorAddIn implements SQLEditorAddIn {
    @Override
    public void init(SQLEditor editor) {
        // Start the document synchronizer and LSP client here.
    }

    @Override
    public void cleanup(SQLEditor editor) {
        // Stop the LSP client and release editor-scoped resources here.
    }

    @Override
    public PrintWriter getServerOutputConsumer() {
        return null;
    }
}

