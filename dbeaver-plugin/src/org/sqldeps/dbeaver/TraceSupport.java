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

import java.util.function.Consumer;

/** Keeps the adapter-wide trace flag and human-readable trace prefix consistent. */
public final class TraceSupport {
    private static final String TRACE_PROPERTY = "sqldeps.lsp.trace";

    private TraceSupport() {
    }

    /** Returns whether verbose adapter tracing is enabled. */
    public static boolean enabled() {
        return Boolean.getBoolean(TRACE_PROPERTY);
    }

    /** Sends a trace message to the adapter log and stderr when tracing is enabled. */
    public static void log(Consumer<String> logConsumer, String message) {
        if (!enabled()) {
            return;
        }

        String traceMessage = format(message);
        if (logConsumer != null) {
            logConsumer.accept(traceMessage);
        }

        System.err.println(traceMessage);
    }

    /** Adds the common trace prefix without choosing an output sink. */
    public static String format(String message) {
        return "sqldeps trace: " + message;
    }
}
