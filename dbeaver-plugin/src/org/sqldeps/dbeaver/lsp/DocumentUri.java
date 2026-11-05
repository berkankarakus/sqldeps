/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package org.sqldeps.dbeaver.lsp;

import java.nio.file.Path;

/** Produces absolute file URIs accepted by the sqldeps LSP server. */
public final class DocumentUri {
    private DocumentUri() {
    }

    /** Converts a real or temporary path to a normalized absolute file URI. */
    public static String forPath(Path path) {
        return path.toAbsolutePath().normalize().toUri().toString();
    }

    /** Creates a stable temporary file URI for an editor without a saved path. */
    public static String forUnsavedEditor(String editorId) {
        Path temporaryPath = Path.of(
            System.getProperty("java.io.tmpdir"),
            "sqldeps-dbeaver",
            editorId + ".sql"
        );

        return forPath(temporaryPath);
    }
}

