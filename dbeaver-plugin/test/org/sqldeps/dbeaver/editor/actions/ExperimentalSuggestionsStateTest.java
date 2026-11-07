package org.sqldeps.dbeaver.editor.actions;

public final class ExperimentalSuggestionsStateTest {
    private ExperimentalSuggestionsStateTest() {
    }

    public static void main(String[] args) {
        boolean original = ExperimentalSuggestionsState.isEnabled();
        try {
            ExperimentalSuggestionsState.setEnabled(!original);
            check(
                ExperimentalSuggestionsState.isEnabled() == !original,
                "state changes are visible to new sessions");
            check(
                ExperimentalSuggestionsState.explanation(true).contains("preview-only"),
                "explanation describes the safety boundary");
            check(
                ExperimentalSuggestionsState.explanation(false).contains("user profile"),
                "explanation describes the preference scope");
        }

        finally {
            ExperimentalSuggestionsState.setEnabled(original);
        }

        System.out.println("sqldeps: experimental suggestion state tests ok");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
