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
package org.sqldeps.dbeaver.ui;

import org.eclipse.ui.IPartListener2;
import org.eclipse.ui.IWindowListener;
import org.eclipse.ui.IWorkbenchPage;
import org.eclipse.ui.IWorkbenchPart;
import org.eclipse.ui.IWorkbenchPartReference;
import org.eclipse.ui.IWorkbenchWindow;
import org.eclipse.ui.PlatformUI;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorBase;
import org.jkiss.dbeaver.ui.editors.sql.SQLEditorNested;

/** Tracks nested SQL source editors and creates their LSP bridges on demand. */
public final class NestedSqlEditorTracker {
    private static final IWindowListener WINDOW_LISTENER = new WindowListener();
    private static boolean installed;

    private NestedSqlEditorTracker() {
    }

    /** Installs workbench listeners exactly once and activates existing nested editors. */
    public static synchronized void install() {
        if (!PlatformUI.isWorkbenchRunning()) {
            return;
        }

        if (installed) {
            return;
        }

        installed = true;

        PlatformUI.getWorkbench().addWindowListener(WINDOW_LISTENER);
        for (IWorkbenchWindow window : PlatformUI.getWorkbench().getWorkbenchWindows()) {
            install(window);
        }
    }

    /** Removes workbench listeners when the plug-in stops. */
    public static synchronized void uninstall() {
        if (!PlatformUI.isWorkbenchRunning()) {
            return;
        }

        if (!installed) {
            return;
        }

        installed = false;

        PlatformUI.getWorkbench().removeWindowListener(WINDOW_LISTENER);
        for (IWorkbenchWindow window : PlatformUI.getWorkbench().getWorkbenchWindows()) {
            uninstall(window);
        }
    }

    private static void install(IWorkbenchWindow window) {
        for (IWorkbenchPage page : window.getPages()) {
            page.addPartListener(PartListener.INSTANCE);
            for (IWorkbenchPartReference reference : page.getEditorReferences()) {
                activate(reference);
            }
        }
    }

    private static void uninstall(IWorkbenchWindow window) {
        for (IWorkbenchPage page : window.getPages()) {
            page.removePartListener(PartListener.INSTANCE);
        }
    }

    private static void attach(IWorkbenchPartReference reference) {
        SQLEditorNested<?> nested = nestedEditor(reference);
        if (nested == null) {
            return;
        }

        NestedSqlEditorLspBridge.attach(nested);
    }

    private static void activate(IWorkbenchPartReference reference) {
        SQLEditorNested<?> nested = nestedEditor(reference);
        if (nested != null) {
            NestedSqlEditorLspBridge.attach(nested);
            NestedSqlEditorLspBridge.activate(nested);
            return;
        }

        NestedSqlEditorLspBridge.clearActive();
    }

    private static void detach(IWorkbenchPartReference reference) {
        SQLEditorNested<?> nested = nestedEditor(reference);
        if (nested != null) {
            NestedSqlEditorLspBridge.detach(nested);
        }
    }

    private static SQLEditorNested<?> nestedEditor(IWorkbenchPartReference reference) {
        IWorkbenchPart part = reference.getPart(false);
        if (part instanceof SQLEditorNested<?> nested) {
            return nested;
        }

        if (part == null) {
            return null;
        }

        SQLEditorBase adapted = part.getAdapter(SQLEditorBase.class);
        return adapted instanceof SQLEditorNested<?> nested ? nested : null;
    }

    private static final class PartListener implements IPartListener2 {
        private static final PartListener INSTANCE = new PartListener();

        @Override
        public void partOpened(IWorkbenchPartReference partRef) {
            attach(partRef);
        }

        @Override
        public void partActivated(IWorkbenchPartReference partRef) {
            activate(partRef);
        }

        @Override
        public void partBroughtToTop(IWorkbenchPartReference partRef) {
            activate(partRef);
        }

        @Override
        public void partVisible(IWorkbenchPartReference partRef) {
            activate(partRef);
        }

        @Override
        public void partInputChanged(IWorkbenchPartReference partRef) {
            activate(partRef);
        }

        @Override
        public void partClosed(IWorkbenchPartReference partRef) {
            detach(partRef);
        }
    }

    private static final class WindowListener implements IWindowListener {
        @Override
        public void windowOpened(IWorkbenchWindow window) {
            install(window);
        }

        @Override
        public void windowClosed(IWorkbenchWindow window) {
            uninstall(window);
        }

        @Override
        public void windowActivated(IWorkbenchWindow window) {
        }

        @Override
        public void windowDeactivated(IWorkbenchWindow window) {
        }
    }
}
