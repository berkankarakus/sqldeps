/*
 * Copyright (c) 2026 Berkan Karakuş
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.sqldeps.dbeaver.editor.actions;

import java.util.Map;

import org.eclipse.lsp4j.CodeAction;
import org.eclipse.lsp4j.Command;

/** Classifies and orders code actions without coupling the choice builder to payload details. */
final class CodeActionCategory {
    private static final int SAFETY_RANK = 0;
    private static final int SUPPRESSION_RANK = 10;
    private static final int REFACTOR_RANK = 20;
    private static final int GENERATION_RANK = 30;
    private static final int EXPLAIN_RANK = 40;
    private static final int CONTEXT_RANK = 50;
    private static final int PREVIEW_RANK = 60;
    private static final int MAINTENANCE_RANK = 70;
    private static final int OTHER_RANK = 80;

    private CodeActionCategory() {
    }

    static int rankOf(CodeAction codeAction, Command command) {
        String category = codeAction == null ? categoryFor(command) : categoryFor(codeAction);

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

    static String categoryFor(CodeAction codeAction) {
        if (codeAction != null && codeAction.getEdit() != null) {
            if (isExperimental(codeAction)) {
                return isRewrite(codeAction) ? "Refactor" : "Generation";
            }

            return "Safety";
        }

        return categoryFor(codeAction == null ? null : codeAction.getCommand());
    }

    static String categoryFor(Command command) {
        if (command == null) {
            return "Action";
        }

        return SqldepsCommands.category(command.getCommand());
    }

    static boolean isSupported(Command command) {
        return command != null && SqldepsCommands.isKnown(command.getCommand());
    }

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

    private static boolean isExperimental(CodeAction codeAction) {
        if (!(codeAction.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        if (Boolean.TRUE.equals(data.get("experimental"))) {
            return true;
        }

        Object generation = data.get("generation");
        return generation instanceof String value && value.contains("rewrite");
    }

    private static boolean isRewrite(CodeAction codeAction) {
        if (!(codeAction.getData() instanceof Map<?, ?> data)) {
            return false;
        }

        Object generation = data.get("generation");
        return generation instanceof String value && value.contains("rewrite");
    }
}
