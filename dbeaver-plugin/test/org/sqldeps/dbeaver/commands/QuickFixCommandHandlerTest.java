package org.sqldeps.dbeaver.commands;

import java.util.List;

import org.eclipse.jface.text.Position;

public final class QuickFixCommandHandlerTest {
    private QuickFixCommandHandlerTest() {
    }

    public static void main(String[] args) {
        Position broad = new Position(0, 24);
        Position specific = new Position(7, 1);

        Position selected = QuickFixCommandHandler.shortestContaining(
            List.of(broad, specific),
            7
        );

        check(selected == specific, "overlapping findings select the most specific annotation");
        check(
            QuickFixCommandHandler.shortestContaining(List.of(broad), 30) == null,
            "unrelated annotations are ignored"
        );

        System.out.println("sqldeps: DBeaver quick-fix annotation selection tests ok");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
