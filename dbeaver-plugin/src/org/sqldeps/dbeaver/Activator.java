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
package org.sqldeps.dbeaver;

import org.eclipse.ui.plugin.AbstractUIPlugin;
import org.osgi.framework.BundleContext;
import org.eclipse.swt.widgets.Display;
import org.sqldeps.dbeaver.editor.NestedSqlEditorTracker;

/** Starts and stops the DBeaver adapter bundle and its nested-editor tracker. */
public final class Activator extends AbstractUIPlugin {
    public static final String PLUGIN_ID = "org.sqldeps.dbeaver";

    private static Activator plugin;

    @Override
    public void start(BundleContext context) throws Exception {
        super.start(context);
        plugin = this;
        Display.getDefault().asyncExec(NestedSqlEditorTracker::install);
    }

    @Override
    public void stop(BundleContext context) throws Exception {
        NestedSqlEditorTracker.uninstall();
        plugin = null;
        super.stop(context);
    }

    /** Returns the active bundle instance, or {@code null} after shutdown. */
    public static Activator getDefault() {
        return plugin;
    }
}

