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
package org.sqldeps.dbeaver.editor;

import org.eclipse.jface.action.IContributionManager;
import org.eclipse.jface.action.IContributionItem;
import org.eclipse.jface.action.MenuManager;
import org.eclipse.jface.action.Separator;
import org.eclipse.ui.PlatformUI;
import org.eclipse.ui.menus.CommandContributionItem;
import org.eclipse.ui.menus.CommandContributionItemParameter;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;

/**
 * Adds the sqldeps submenu to the context menu of nested source editors.
 *
 * <p>Nested editors do not use the SQL editor's menu id, so a plugin.xml popup contribution never reaches
 * them. DBeaver's own context menu contributor hook works for every editor instance.
 */
final class NestedEditorContextMenu {
    private static final String MENU_ID = "org.sqldeps.dbeaver.nestedEditorContextMenu";

    private NestedEditorContextMenu() {
    }

    static void install(SQLEditorNested<?> editor) {
        editor.addContextMenuContributor(NestedEditorContextMenu::contribute);
    }

    private static void contribute(IContributionManager manager) {
        MenuManager submenu = new MenuManager("sqldeps", MENU_ID);

        addCommand(submenu, "org.sqldeps.dbeaver.commands.analyzeSql", "Analyze SQL");
        submenu.add(new Separator());
        addCommand(submenu, "org.sqldeps.dbeaver.commands.codeActions", "Show All sqldeps Code Actions...");
        addCommand(submenu, "org.sqldeps.dbeaver.commands.quickFixes", "Show sqldeps Quick Fixes...");
        submenu.add(new Separator());
        addCommand(submenu, "org.sqldeps.dbeaver.commands.impact", "Show Impact of Changing Selected Object...");
        addCommand(submenu, "org.sqldeps.dbeaver.commands.format", "Format SQL Script");
        submenu.add(new Separator());
        addCommand(submenu, "org.sqldeps.dbeaver.commands.diagnostics", "Show sqldeps Diagnostics");
        submenu.add(new Separator());
        addCommand(submenu, "org.sqldeps.dbeaver.commands.selectIndex", "Select sqldeps Index...");
        addCommand(submenu, "org.sqldeps.dbeaver.commands.reindex", "Reindex sqldeps Index");

        addAtTop(manager, submenu);
    }

    private static void addAtTop(IContributionManager manager, MenuManager submenu) {
        if (manager instanceof MenuManager menuManager) {
            for (IContributionItem item : menuManager.getItems()) {
                String itemId = item.getId();
                if (itemId == null || itemId.isBlank()) {
                    continue;
                }

                menuManager.insertBefore(itemId, new Separator());
                menuManager.insertBefore(itemId, submenu);

                return;
            }
        }

        manager.add(new Separator());
        manager.add(submenu);
    }

    private static void addCommand(MenuManager menu, String commandId, String label) {
        CommandContributionItemParameter parameter = new CommandContributionItemParameter(
            PlatformUI.getWorkbench(),
            null,
            commandId,
            CommandContributionItem.STYLE_PUSH
        );
        parameter.label = label;

        menu.add(new CommandContributionItem(parameter));
    }
}
