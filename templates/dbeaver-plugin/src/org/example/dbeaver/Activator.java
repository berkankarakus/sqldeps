/*
 * Copyright (c) 2026.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 */
package org.example.dbeaver;

import org.eclipse.ui.plugin.AbstractUIPlugin;
import org.osgi.framework.BundleContext;

/** Starts and stops the DBeaver plugin bundle. */
public final class Activator extends AbstractUIPlugin {
    public static final String PLUGIN_ID = "org.example.dbeaver";

    private static Activator plugin;

    @Override
    public void start(BundleContext context) throws Exception {
        super.start(context);
        plugin = this;
    }

    @Override
    public void stop(BundleContext context) throws Exception {
        plugin = null;
        super.stop(context);
    }

    /** Returns the active plugin instance, or {@code null} after shutdown. */
    public static Activator getDefault() {
        return plugin;
    }
}

