package org.sqldeps.dbeaver.editor.actions;

import java.util.List;
import java.util.Map;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

import org.eclipse.jface.text.Document;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;

public final class WorkspaceEditSafetyTest {
    private WorkspaceEditSafetyTest() {
    }

    public static void main(String[] args) throws Exception {
        applicabilityRejectsStaleAndCrossDocumentEdits();
        resolveUsesUtf16OffsetsAndAppliesEditsFromRightToLeft();
        resolveRejectsInvalidAndOverlappingEdits();
        externalConfigurationEditsAreBoundedToDotSqldeps();
        missingExternalConfigurationCanBeCreatedSafely();

        System.out.println("sqldeps: DBeaver workspace edit safety tests ok");
    }

    private static void applicabilityRejectsStaleAndCrossDocumentEdits() {
        DocumentSnapshot snapshot = new DocumentSnapshot("file:///query.sql", 2, 2, "SELECT 1;");
        WorkspaceEdit edit = workspaceEdit(
            Map.of(snapshot.uri(), List.of(textEdit(0, 0, 0, 1, "X")))
        );

        check(
            WorkspaceEditSafety.isApplicable(snapshot, true, edit),
            "current single-document edit is applicable");
        check(
            !WorkspaceEditSafety.isApplicable(snapshot, false, edit),
            "stale edit is rejected");
        check(
            !WorkspaceEditSafety.isApplicable(
                snapshot,
                true,
                workspaceEdit(Map.of(
                    snapshot.uri(), List.of(textEdit(0, 0, 0, 1, "X")),
                    "file:///other.sql", List.of(textEdit(0, 0, 0, 1, "Y"))
                ))),
            "cross-document edit is rejected");
    }

    private static void resolveUsesUtf16OffsetsAndAppliesEditsFromRightToLeft() {
        Document document = new Document("SELECT 😀;");
        List<WorkspaceEditSafety.ResolvedTextEdit> edits = WorkspaceEditSafety.resolve(
            document,
            List.of(
                textEdit(0, 7, 0, 9, "name"),
                textEdit(0, 0, 0, 6, "FETCH")
            )
        );

        check(edits != null, "valid edits resolve");
        check(edits.get(0).start() == 7, "edits are ordered from right to left");
        check(edits.get(0).end() == 9, "UTF-16 end offset is preserved");
    }

    private static void resolveRejectsInvalidAndOverlappingEdits() {
        Document document = new Document("SELECT 1;");

        check(
            WorkspaceEditSafety.resolve(
                document,
                List.of(textEdit(0, 8, 0, 7, "x"))) == null,
            "reversed edit range is rejected");
        check(
            WorkspaceEditSafety.resolve(
                document,
                List.of(
                    textEdit(0, 0, 0, 4, "x"),
                    textEdit(0, 3, 0, 6, "y")
                )) == null,
            "overlapping edits are rejected");
    }

    private static void externalConfigurationEditsAreBoundedToDotSqldeps() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-workspace-edit");
        Path configuration = directory.resolve(".sqldeps");
        Files.writeString(configuration, "index=old\n", StandardCharsets.UTF_8);
        DocumentSnapshot snapshot = new DocumentSnapshot(
            "file:///query.sql",
            2,
            2,
            "SELECT 1;");
        WorkspaceEdit edit = workspaceEdit(Map.of(
            configuration.toUri().toString(),
            List.of(textEdit(0, 6, 0, 9, "new"))));

        WorkspaceEditSafety.ExternalEdit resolved = WorkspaceEditSafety.resolveExternalConfigurationEdit(
            snapshot,
            true,
            edit);
        check(resolved != null, "dot sqldeps edit is applicable");
        check(resolved.path().equals(configuration), "external target path is preserved");
        check(!resolved.createIfMissing(), "existing dot sqldeps file is not recreated");
        check(
            WorkspaceEditSafety.resolveExternalConfigurationEdit(
                snapshot,
                true,
                workspaceEdit(Map.of(
                    directory.resolve("settings.json").toUri().toString(),
                    List.of(textEdit(0, 0, 0, 0, "x"))))) == null,
            "non sqldeps external target is rejected");

        Files.deleteIfExists(configuration);
        Files.deleteIfExists(directory);
    }

    private static void missingExternalConfigurationCanBeCreatedSafely() throws Exception {
        Path directory = Files.createTempDirectory("sqldeps-workspace-edit-create");
        Path configuration = directory.resolve(".sqldeps");
        DocumentSnapshot snapshot = new DocumentSnapshot(
            "file:///query.sql",
            2,
            2,
            "SELECT 1;");
        WorkspaceEdit edit = workspaceEdit(Map.of(
            configuration.toUri().toString(),
            List.of(textEdit(0, 0, 0, 0, "sqldeps: disable-repo=nolock\n"))));

        WorkspaceEditSafety.ExternalEdit resolved = WorkspaceEditSafety.resolveExternalConfigurationEdit(
            snapshot,
            true,
            edit);
        check(resolved != null, "missing dot sqldeps edit is applicable");
        check(resolved.createIfMissing(), "missing dot sqldeps file is marked for creation");
        check(resolved.originalText().isEmpty(), "missing dot sqldeps file starts empty");

        Files.deleteIfExists(configuration);
        Files.deleteIfExists(directory);
    }

    private static WorkspaceEdit workspaceEdit(Map<String, List<TextEdit>> changes) {
        WorkspaceEdit edit = new WorkspaceEdit();
        edit.setChanges(changes);
        return edit;
    }

    private static TextEdit textEdit(
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        String newText
    ) {
        return new TextEdit(
            new Range(
                new Position(startLine, startCharacter),
                new Position(endLine, endCharacter)
            ),
            newText
        );
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
