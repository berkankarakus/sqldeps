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
package org.sqldeps.dbeaver.editor.actions;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

import org.eclipse.jface.text.IDocument;
import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.Diagnostic;
import org.eclipse.lsp4j.Range;
import org.eclipse.lsp4j.TextEdit;
import org.eclipse.lsp4j.Command;
import org.eclipse.lsp4j.jsonrpc.messages.Either;
import org.sqldeps.dbeaver.editor.diagnostics.DiagnosticSeverityName;

final class CodeActionChoiceBuilder {
    /** Safety actions first, then suppressions by scope, refactors, generators, explanations, context and previews. */
    private static final int SAFETY_RANK = 0;
    private static final int SUPPRESSION_RANK = 10;
    private static final int REFACTOR_RANK = 20;
    private static final int GENERATION_RANK = 30;
    private static final int EXPLAIN_RANK = 40;
    private static final int CONTEXT_RANK = 50;
    private static final int PREVIEW_RANK = 60;
    private static final int MAINTENANCE_RANK = 70;
    private static final int OTHER_RANK = 80;

    private final IDocument document;

    CodeActionChoiceBuilder(IDocument document) {
        this.document = document;
    }

    /** Builds the displayable choices in a meaningful order; equal ranks keep the server's order. */
    List<CodeActionChoice> build(
        List<Either<Command, CodeAction>> actions,
        List<Diagnostic> diagnostics
    ) {
        List<CodeActionChoice> choices = new ArrayList<>();
        if (actions == null) {
            return choices;
        }

        List<RankedChoice> ranked = new ArrayList<>();
        for (Either<Command, CodeAction> action : actions) {
            addChoice(ranked, action, diagnostics);
        }

        ranked.sort(Comparator.comparingInt(RankedChoice::rank));
        for (RankedChoice rankedChoice : ranked) {
            choices.add(rankedChoice.choice());
        }

        return choices;
    }

    private record RankedChoice(int rank, CodeActionChoice choice) {
    }

    private void addChoice(
        List<RankedChoice> choices,
        Either<Command, CodeAction> action,
        List<Diagnostic> diagnostics
    ) {
        if (action.isLeft()) {
            addCommandChoice(choices, action.getLeft(), diagnostics);
            return;
        }

        if (!action.isRight()) {
            return;
        }

        addCodeActionChoice(choices, action.getRight(), diagnostics);
    }

    private void addCommandChoice(
        List<RankedChoice> choices,
        Command command,
        List<Diagnostic> diagnostics
    ) {
        if (!isSupportedCommand(command)) {
            return;
        }

        choices.add(new RankedChoice(
            rankOf(null, command),
            new CodeActionChoice(describeCommand(command, diagnostics), null, command)
        ));
    }

    private void addCodeActionChoice(
        List<RankedChoice> choices,
        CodeAction codeAction,
        List<Diagnostic> diagnostics
    ) {
        if (codeAction.getEdit() != null) {
            choices.add(new RankedChoice(
                rankOf(codeAction, null),
                new CodeActionChoice(
                    describeAction(codeAction.getTitle(), codeAction, diagnostics),
                    codeAction,
                    null
                )
            ));
            return;
        }

        if (!isSupportedCommand(codeAction.getCommand())) {
            return;
        }

        choices.add(new RankedChoice(
            rankOf(codeAction, null),
            new CodeActionChoice(
                describeAction(codeAction.getTitle(), codeAction, diagnostics),
                null,
                codeAction.getCommand()
            )
        ));
    }

    private int rankOf(CodeAction codeAction, Command command) {
        String category = codeAction != null ? categoryFor(codeAction) : categoryFor(command);

        return switch (category) {
            case "Safety" -> SAFETY_RANK + suppressionOffset(codeAction);
            case "Refactor" -> REFACTOR_RANK;
            case "Generation" -> GENERATION_RANK;
            case "Explain" -> EXPLAIN_RANK;
            case "Context" -> CONTEXT_RANK;
            case "Preview" -> PREVIEW_RANK;
            case "Maintenance" -> MAINTENANCE_RANK;
            default -> OTHER_RANK;
        };
    }

    /** Real fixes keep rank 0; suppressions follow by scope: line, then file, then repository. */
    private static int suppressionOffset(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return 0;
        }

        Object scope = data.get("suppressionScope");
        if (!(scope instanceof String name)) {
            return 0;
        }

        return switch (name) {
            case "line" -> SUPPRESSION_RANK;
            case "file" -> SUPPRESSION_RANK + 1;
            case "repository" -> SUPPRESSION_RANK + 2;
            default -> 0;
        };
    }

    private String describeAction(
        String title,
        CodeAction codeAction,
        List<Diagnostic> diagnostics
    ) {
        return describeAction(categoryFor(codeAction), title, codeAction, diagnostics);
    }

    private String describeCommand(Command command, List<Diagnostic> diagnostics) {
        return describeAction(categoryFor(command), command.getTitle(), null, diagnostics);
    }

    private String describeAction(
        String category,
        String title,
        CodeAction codeAction,
        List<Diagnostic> diagnostics
    ) {
        String description = actionDescription(category, title, codeAction);
        Diagnostic diagnostic = relatedDiagnostic(codeAction, diagnostics);

        if (diagnostic == null) {
            return description;
        }

        return description + diagnosticDescription(diagnostic);
    }

    private String actionDescription(String category, String title, CodeAction codeAction) {
        return "[" + category + "] " + title
            + modeDescription(category, codeAction)
            + evidenceDescription(codeAction)
            + applicabilityDescription(codeAction)
            + explanationDescription(codeAction);
    }

    private static String diagnosticDescription(Diagnostic diagnostic) {
        String message = diagnostic.getMessage();

        if (message.length() > 160) {
            message = message.substring(0, 157) + "...";
        }

        return " — [" + DiagnosticSeverityName.of(diagnostic.getSeverity()) + "] " + message;
    }

    private String modeDescription(String category, CodeAction codeAction) {
        if (codeAction != null && codeAction.getEdit() != null) {
            return " — Applies edit";
        }

        if ("Preview".equals(category)) {
            return " — Preview only";
        }

        return " — Runs command";
    }

    private String categoryFor(CodeAction codeAction) {
        if (codeAction != null && codeAction.getEdit() != null) {
            if (isExperimental(codeAction)) {
                return isRewrite(codeAction) ? "Refactor" : "Generation";
            }

            return "Safety";
        }

        return categoryFor(codeAction == null ? null : codeAction.getCommand());
    }

    private String categoryFor(Command command) {
        if (command == null) {
            return "Action";
        }

        return SqldepsCommands.category(command.getCommand());
    }

    private boolean isExperimental(CodeAction codeAction) {
        if (!(codeAction.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        if (Boolean.TRUE.equals(data.get("experimental"))) {
            return true;
        }

        Object generation = data.get("generation");
        return generation instanceof String value && value.contains("rewrite");
    }

    private boolean isRewrite(CodeAction codeAction) {
        if (!(codeAction.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        Object generation = data.get("generation");
        return generation instanceof String value && value.contains("rewrite");
    }

    private String evidenceDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        Object referencesValue = data.get("references");
        if (!(referencesValue instanceof List<?> references) || references.isEmpty()) {
            return "";
        }

        String firstReferenceTitle = referenceTitle(references.get(0));
        String description = firstReferenceTitle.isBlank()
            ? references.size() + " source(s)"
            : firstReferenceTitle;
        if (references.size() > 1) {
            description += " +" + (references.size() - 1) + " more";
        }

        return " — Evidence: " + description;
    }

    private String explanationDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        if (!(data.get("explanation") instanceof Map<?, ?> explanation)) {
            return "";
        }

        Object summary = explanation.get("summary");
        if (!(summary instanceof String value) || value.isBlank()) {
            return "";
        }

        String shortened = value.length() > 120
            ? value.substring(0, 117) + "..."
            : value;

        return " — Why: " + shortened;
    }

    private String applicabilityDescription(CodeAction codeAction) {
        if (codeAction == null || !(codeAction.getData() instanceof Map<?, ?> data)) {
            return "";
        }

        Object applicability = data.get("applicability");
        if (!(applicability instanceof String value) || value.isBlank()) {
            return "";
        }

        String shortened = value.length() > 140
            ? value.substring(0, 137) + "..."
            : value;

        return " — Applies when: " + shortened;
    }

    private static String referenceTitle(Object reference) {
        if (!(reference instanceof Map<?, ?> referenceMap)) {
            return "";
        }

        Object title = referenceMap.get("Title");
        if (title == null) {
            title = referenceMap.get("title");
        }

        return title instanceof String value ? value : "";
    }

    private Diagnostic relatedDiagnostic(CodeAction codeAction, List<Diagnostic> diagnostics) {
        Diagnostic explicit = firstDiagnostic(codeAction);
        if (explicit != null) {
            return explicit;
        }

        if (diagnostics.size() == 1) {
            return diagnostics.get(0);
        }

        return diagnosticForEdits(codeAction, diagnostics);
    }

    private static Diagnostic firstDiagnostic(CodeAction codeAction) {
        if (codeAction == null || codeAction.getDiagnostics() == null || codeAction.getDiagnostics().isEmpty()) {
            return null;
        }

        return codeAction.getDiagnostics().get(0);
    }

    private Diagnostic diagnosticForEdits(CodeAction codeAction, List<Diagnostic> diagnostics) {
        if (codeAction == null || codeAction.getEdit() == null || codeAction.getEdit().getChanges() == null) {
            return null;
        }

        for (List<TextEdit> edits : codeAction.getEdit().getChanges().values()) {
            Diagnostic match = diagnosticForEdits(edits, diagnostics);
            if (match != null) {
                return match;
            }
        }

        return null;
    }

    private Diagnostic diagnosticForEdits(List<TextEdit> edits, List<Diagnostic> diagnostics) {
        if (edits == null) {
            return null;
        }

        for (TextEdit edit : edits) {
            Diagnostic match = diagnosticForEdit(edit, diagnostics);
            if (match != null) {
                return match;
            }
        }

        return null;
    }

    private Diagnostic diagnosticForEdit(TextEdit edit, List<Diagnostic> diagnostics) {
        if (edit == null || edit.getRange() == null) {
            return null;
        }

        for (Diagnostic diagnostic : diagnostics) {
            if (sameLineOrOverlapping(edit.getRange(), diagnostic.getRange())) {
                return diagnostic;
            }
        }

        return null;
    }

    private boolean sameLineOrOverlapping(Range first, Range second) {
        if (sameLines(first, second)) {
            return true;
        }

        return DiagnosticRangeSelection.overlaps(document, first, second);
    }

    private static boolean sameLines(Range first, Range second) {
        return first.getStart().getLine() == second.getStart().getLine()
            && first.getEnd().getLine() == second.getEnd().getLine();
    }

    private boolean isSupportedCommand(Command command) {
        return command != null && isKnownCommand(command.getCommand());
    }

    private static boolean isKnownCommand(String command) {
        return SqldepsCommands.isKnown(command);
    }
}
