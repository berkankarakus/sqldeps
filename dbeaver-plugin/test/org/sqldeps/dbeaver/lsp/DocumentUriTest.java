package org.sqldeps.dbeaver.lsp;

import java.nio.file.Path;

public final class DocumentUriTest {
    private DocumentUriTest() {
    }

    public static void main(String[] args) {
        pathUrisAreAbsoluteAndNormalized();
        unsavedEditorUrisUseTheTemporaryDirectory();

        System.out.println("sqldeps: Java document URI tests ok");
    }

    private static void pathUrisAreAbsoluteAndNormalized() {
        Path path = Path.of(".", "nested", "..", "query.sql");

        String actual = DocumentUri.forPath(path);
        String expected = path.toAbsolutePath().normalize().toUri().toString();

        check(expected.equals(actual), "normalized path URI");
    }

    private static void unsavedEditorUrisUseTheTemporaryDirectory() {
        String uri = DocumentUri.forUnsavedEditor("editor-42");

        check(uri.startsWith("file:"), "unsaved editor URI scheme");
        check(uri.endsWith("/sqldeps-dbeaver/editor-42.sql"), "unsaved editor URI path");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
