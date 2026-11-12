package org.sqldeps.dbeaver.editor.actions;

import java.util.List;

import org.eclipse.jface.text.Document;
import org.eclipse.jface.text.TextSelection;
import org.eclipse.lsp4j.Position;
import org.eclipse.lsp4j.Range;

public final class CodeActionRangeResolverTest {
    private CodeActionRangeResolverTest() {
    }

    public static void main(String[] args) throws Exception {
        selectionUsesDocumentLines();
        offsetsAreClampedToDocumentBounds();

        System.out.println("sqldeps: DBeaver code action range resolver tests ok");
    }

    private static void selectionUsesDocumentLines() throws Exception {
        CodeActionRangeResolver resolver = resolver("SELECT 1;\nSELECT 2;");

        Range range = resolver.selectedRange(new TextSelection(10, 8));

        check(range.getStart().equals(new Position(1, 0)), "selection starts on the second line");
        check(range.getEnd().equals(new Position(1, 8)), "selection ends at the selected document offset");
    }

    private static void offsetsAreClampedToDocumentBounds() throws Exception {
        CodeActionRangeResolver resolver = resolver("SELECT 1;");

        Range range = resolver.rangeAt(-10, 1000);

        check(range.getStart().equals(new Position(0, 0)), "negative offsets clamp to the document start");
        check(range.getEnd().equals(new Position(0, 9)), "oversized ranges clamp to the document end");
    }

    private static CodeActionRangeResolver resolver(String text) {
        return new CodeActionRangeResolver(new Document(text), List::of);
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
