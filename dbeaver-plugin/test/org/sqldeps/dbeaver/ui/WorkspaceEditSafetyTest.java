package org.sqldeps.dbeaver.ui;

import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.Document;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.sqldeps.dbeaver.lsp.DocumentSnapshot;

public final class WorkspaceEditSafetyTest {
    private WorkspaceEditSafetyTest() {
    }

    public static void main(String[] args) {
        applicabilityRejectsStaleAndCrossDocumentEdits();
        resolveUsesUtf16OffsetsAndAppliesEditsFromRightToLeft();
        resolveRejectsInvalidAndOverlappingEdits();

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
