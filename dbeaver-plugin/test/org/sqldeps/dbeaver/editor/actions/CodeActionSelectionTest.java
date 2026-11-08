package org.sqldeps.dbeaver.editor.actions;

import java.util.List;

public final class CodeActionSelectionTest {
    private CodeActionSelectionTest() {
    }

    public static void main(String[] args) {
        emptyChoicesAreNotPresented();
        singleChoiceIsAppliedDirectly();
        multipleChoicesAskTheUser();
        cancelledDialogConfirmsNothing();
        emptyOrForeignResultConfirmsNothing();
        confirmedDialogReturnsFirstChoice();

        System.out.println("sqldeps: DBeaver code action selection tests ok");
    }

    private static void emptyChoicesAreNotPresented() {
        check(CodeActionSelection.modeFor(null) == CodeActionSelection.Mode.NONE, "null choices are not presented");
        check(CodeActionSelection.modeFor(List.of()) == CodeActionSelection.Mode.NONE, "empty choices are not presented");
    }

    private static void singleChoiceIsAppliedDirectly() {
        CodeActionSelection.Mode mode = CodeActionSelection.modeFor(List.of(choice("only")));

        check(mode == CodeActionSelection.Mode.APPLY_DIRECTLY, "a single choice skips the dialog");
    }

    private static void multipleChoicesAskTheUser() {
        CodeActionSelection.Mode mode = CodeActionSelection.modeFor(List.of(choice("a"), choice("b")));

        check(mode == CodeActionSelection.Mode.ASK_USER, "several choices open the dialog");
    }

    private static void cancelledDialogConfirmsNothing() {
        Object[] selected = { choice("a") };

        check(CodeActionSelection.confirmedChoice(false, selected) == null, "cancel never applies a choice");
    }

    private static void emptyOrForeignResultConfirmsNothing() {
        check(CodeActionSelection.confirmedChoice(true, null) == null, "null result confirms nothing");
        check(CodeActionSelection.confirmedChoice(true, new Object[0]) == null, "empty result confirms nothing");
        check(CodeActionSelection.confirmedChoice(true, new Object[] { "text" }) == null, "foreign element confirms nothing");
    }

    private static void confirmedDialogReturnsFirstChoice() {
        CodeActionChoice first = choice("first");
        Object[] selected = { first, choice("second") };

        check(CodeActionSelection.confirmedChoice(true, selected) == first, "confirmed dialog returns the first choice");
    }

    private static CodeActionChoice choice(String title) {
        return new CodeActionChoice(title, null, null);
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError(message);
        }
    }
}
