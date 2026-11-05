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

import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

/** Shares scheduled diagnostic work without sharing editor document state. */
final class SharedLspScheduler {
    private static final ScheduledExecutorService INSTANCE = Executors.newScheduledThreadPool(
        4,
        runnable -> {
            Thread thread = new Thread(runnable, "sqldeps-dbeaver");
            thread.setDaemon(true);
            return thread;
        }
    );

    private SharedLspScheduler() {
    }

    static ScheduledExecutorService instance() {
        return INSTANCE;
    }
}
