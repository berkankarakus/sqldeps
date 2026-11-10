package org.sqldeps.dbeaver.editor.actions;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.Document;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.WorkspaceEdit;
import org.eclipse.lsp4j.jsonrpc.messages.Either;

public final class CodeActionOrderTest {
    private CodeActionOrderTest() {
    }

    public static void main(String[] args) {
        actionsAreOrderedByMeaning();
        suppressionsFollowLineFileRepository();
        equalRanksKeepTheServerOrder();

        System.out.println("sqldeps: DBeaver code action order tests ok");
    }

    private static void actionsAreOrderedByMeaning() {
        List<Either<Command, CodeAction>> actions = List.of(
            command("Reindex", "sqldeps.reindex"),
            command("Who writes", "sqldeps.who-writes"),
            command("Query preview", "sqldeps.query-builder"),
            command("Explain", "sqldeps.explain-diagnostic"),
            edit("Generated rewrite", Map.of("experimental", true)),
            edit("Disable for repository", Map.of("suppressionScope", "repository")),
            edit("Replace SELECT *", Map.of())
        );

        List<String> titles = titles(actions);

        check(index(titles, "Replace SELECT *") == 0, "the real fix is listed first");
        check(index(titles, "Disable for repository") == 1, "suppressions follow the fixes");
        check(index(titles, "Generated rewrite") == 2, "generated rewrites follow suppressions");
        check(index(titles, "Explain") == 3, "reviews follow generated rewrites");
        check(index(titles, "Who writes") == 4, "context actions follow reviews");
        check(index(titles, "Query preview") == 5, "previews follow context actions");
        check(index(titles, "Reindex") == 6, "maintenance is listed last");
    }

    private static void suppressionsFollowLineFileRepository() {
        List<Either<Command, CodeAction>> actions = List.of(
            edit("Disable for repository", Map.of("suppressionScope", "repository")),
            edit("Disable for file", Map.of("suppressionScope", "file")),
            edit("Disable for line", Map.of("suppressionScope", "line"))
        );

        List<String> titles = titles(actions);

        check(index(titles, "Disable for line") == 0, "line suppression comes first");
        check(index(titles, "Disable for file") == 1, "file suppression comes next");
        check(index(titles, "Disable for repository") == 2, "repository suppression comes last");
    }

    private static void equalRanksKeepTheServerOrder() {
        List<Either<Command, CodeAction>> actions = List.of(
            edit("Fix B", Map.of()),
            edit("Fix A", Map.of()),
            command("Who reads", "sqldeps.who-reads"),
            command("Who writes", "sqldeps.who-writes")
        );

        List<String> titles = titles(actions);

        check(index(titles, "Fix B") < index(titles, "Fix A"), "fixes keep the server order");
        check(index(titles, "Who reads") < index(titles, "Who writes"), "context actions keep the server order");
    }

    private static List<String> titles(List<Either<Command, CodeAction>> actions) {
        List<String> titles = new ArrayList<>();

        for (CodeActionChoice choice : new CodeActionChoiceBuilder(new Document("")).build(actions, List.of())) {
            String title = choice.title();
            int start = title.indexOf("] ") + 2;
            int end = title.indexOf(" — ");

            titles.add(title.substring(start, end < 0 ? title.length() : end));
        }

        return titles;
    }

    private static int index(List<String> titles, String title) {
        int index = titles.indexOf(title);

        check(index >= 0, "action is listed: " + title);

        return index;
    }

    private static Either<Command, CodeAction> command(String title, String commandName) {
        return Either.forLeft(new Command(title, commandName));
    }

    private static Either<Command, CodeAction> edit(String title, Map<String, Object> data) {
        CodeAction action = new CodeAction(title);
        action.setEdit(new WorkspaceEdit());
        action.setData(data);

        return Either.forRight(action);
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
