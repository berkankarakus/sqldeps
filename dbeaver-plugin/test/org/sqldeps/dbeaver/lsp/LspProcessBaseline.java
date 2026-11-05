package org.sqldeps.dbeaver.lsp;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import org.eclipse.lsp4j.DocumentDiagnosticReport;

public final class LspProcessBaseline {
    private LspProcessBaseline() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 2 || args.length > 5) {
            throw new IllegalArgumentException(
                "Expected: <sqldeps-executable> <process-count> [index-path] [automatic-index-selection] [shared-process]"
            );
        }

        Path executable = Path.of(args[0]);
        int processCount = Integer.parseInt(args[1]);
        String indexPath = args.length >= 3 && !args[2].isBlank() ? args[2] : null;
        boolean automaticIndexSelection = args.length < 4 || Boolean.parseBoolean(args[3]);
        boolean sharedProcess = args.length < 5 || Boolean.parseBoolean(args[4]);

        if (processCount < 1) {
            throw new IllegalArgumentException("Process count must be positive.");
        }

        measure(executable, processCount, indexPath, automaticIndexSelection, sharedProcess);
    }

    private static void measure(
        Path executable,
        int processCount,
        String indexPath,
        boolean automaticIndexSelection,
        boolean sharedProcess
    ) throws Exception {
        List<LspConnection> connections = new ArrayList<>(processCount);
        long startupStarted = System.nanoTime();

        try {
            for (int index = 0; index < processCount; index++) {
                connections.add(start(
                    executable,
                    indexPath,
                    automaticIndexSelection,
                    sharedProcess
                ));
            }

            long startupElapsed = System.nanoTime() - startupStarted;
            List<CompletableFuture<DocumentDiagnosticReport>> requests = openDocuments(connections);
            long diagnosticsStarted = System.nanoTime();
            CompletableFuture.allOf(requests.toArray(CompletableFuture[]::new))
                .get(30, TimeUnit.SECONDS);
            long diagnosticsElapsed = System.nanoTime() - diagnosticsStarted;
            ProcessMetrics metrics = sampleProcessMetrics(executable);
            int activeProcessCount = metrics.processCount() >= 0
                ? metrics.processCount()
                : sharedProcess ? LspProcessPool.activeProcessCount() : processCount;

            System.out.printf(
                "mode=%s processes=%d active_processes=%d threads=%s rss_kb=%s startup_ms=%.1f startup_avg_ms=%.1f diagnostics_ms=%.1f diagnostics_avg_ms=%.1f%n",
                sharedProcess ? "shared" : "independent",
                processCount,
                activeProcessCount,
                formatMetric(metrics.threadCount()),
                formatMetric(metrics.rssKilobytes()),
                milliseconds(startupElapsed),
                milliseconds(startupElapsed) / processCount,
                milliseconds(diagnosticsElapsed),
                milliseconds(diagnosticsElapsed) / processCount
            );
        }

        finally {
            for (LspConnection connection : connections) {
                connection.close();
            }
        }
    }

    private static LspConnection start(
        Path executable,
        String indexPath,
        boolean automaticIndexSelection,
        boolean sharedProcess
    ) throws Exception {
        if (sharedProcess) {
            return LspProcessPool.acquire(
                List.of(executable.toString(), "lsp"),
                null,
                indexPath,
                automaticIndexSelection,
                ignored -> {
                }
            );
        }

        return LspProcessManager.start(
            List.of(executable.toString(), "lsp"),
            null,
            indexPath,
            automaticIndexSelection,
            ignored -> {
            },
            ignored -> {
            }
        );
    }

    private static List<CompletableFuture<DocumentDiagnosticReport>> openDocuments(
        List<LspConnection> connections)
    {
        List<CompletableFuture<DocumentDiagnosticReport>> requests = new ArrayList<>(connections.size());

        for (int index = 0; index < connections.size(); index++) {
            DocumentSnapshot snapshot = new DocumentSnapshot(
                "file:///tmp/sqldeps-baseline-" + index + ".sql",
                1,
                1,
                "SELECT 1;"
            );
            LspConnection connection = connections.get(index);
            connection.didOpen(snapshot);
            requests.add(connection.requestDiagnostics(snapshot.uri()));
        }

        return requests;
    }

    private static double milliseconds(long nanoseconds) {
        return nanoseconds / 1_000_000.0;
    }

    private static ProcessMetrics sampleProcessMetrics(Path executable) {
        List<ProcessHandle> processes;
        try {
            processes = findLspProcesses(executable);
        }

        catch (RuntimeException exception) {
            return ProcessMetrics.unavailable();
        }

        long rssKilobytes = 0;
        int threadCount = 0;
        boolean rssKnown = true;
        boolean threadsKnown = true;

        for (ProcessHandle process : processes) {
            ProcessStats stats = readProcessStats(process);
            if (stats.rssKilobytes() < 0) {
                rssKnown = false;
            }
            else {
                rssKilobytes += stats.rssKilobytes();
            }

            if (stats.threadCount() < 0) {
                threadsKnown = false;
            }
            else {
                threadCount += stats.threadCount();
            }
        }

        return new ProcessMetrics(
            processes.size(),
            rssKnown ? rssKilobytes : -1,
            threadsKnown ? threadCount : -1
        );
    }

    private static List<ProcessHandle> findLspProcesses(Path executable) {
        String executableName = executable.getFileName().toString();
        List<ProcessHandle> children = ProcessHandle.current().children()
            .filter(ProcessHandle::isAlive)
            .toList();
        List<ProcessHandle> matches = children.stream()
            .filter(process -> isExecutable(process, executableName))
            .toList();

        return matches.isEmpty() ? children : matches;
    }

    private static boolean isExecutable(ProcessHandle process, String executableName) {
        String command = process.info().command().orElse("");
        if (!command.isBlank()) {
            return Path.of(command).getFileName().toString().equals(executableName);
        }

        return process.info().commandLine().orElse("").contains(executableName);
    }

    private static ProcessStats readProcessStats(ProcessHandle process) {
        ProcessStats procStats = readProcStatus(process.pid());
        if (procStats.hasKnownValue()) {
            return procStats;
        }

        return readPsStats(process.pid());
    }

    private static ProcessStats readProcStatus(long processId) {
        Path statusPath = Path.of("/proc", Long.toString(processId), "status");
        if (!Files.isRegularFile(statusPath)) {
            return ProcessStats.unavailable();
        }

        long rssKilobytes = -1;
        int threadCount = -1;
        try {
            for (String line : Files.readAllLines(statusPath, StandardCharsets.UTF_8)) {
                if (line.startsWith("VmRSS:")) {
                    rssKilobytes = parseLongValue(line);
                }
                else if (line.startsWith("Threads:")) {
                    threadCount = (int)parseLongValue(line);
                }
            }
        }

        catch (IOException exception) {
            return ProcessStats.unavailable();
        }

        return new ProcessStats(rssKilobytes, threadCount);
    }

    private static ProcessStats readPsStats(long processId) {
        String threadField = isMacOs() ? "thcount" : "nlwp";
        try {
            Process process = new ProcessBuilder(
                "ps",
                "-p",
                Long.toString(processId),
                "-o",
                "rss=",
                "-o",
                threadField + "="
            ).redirectErrorStream(true).start();
            String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8).trim();
            if (!process.waitFor(1, TimeUnit.SECONDS)) {
                process.destroyForcibly();
                return ProcessStats.unavailable();
            }

            String[] values = output.split("\\s+");
            if (process.exitValue() != 0 || values.length < 2) {
                return ProcessStats.unavailable();
            }

            return new ProcessStats(Long.parseLong(values[0]), Integer.parseInt(values[1]));
        }

        catch (IOException | InterruptedException | NumberFormatException exception) {
            if (exception instanceof InterruptedException) {
                Thread.currentThread().interrupt();
            }

            return ProcessStats.unavailable();
        }
    }

    private static long parseLongValue(String line) {
        String[] parts = line.trim().split("\\s+");
        if (parts.length < 2) {
            return -1;
        }

        try {
            return Long.parseLong(parts[1]);
        }

        catch (NumberFormatException exception) {
            return -1;
        }
    }

    private static boolean isMacOs() {
        return System.getProperty("os.name", "")
            .toLowerCase(Locale.ROOT)
            .contains("mac");
    }

    private static String formatMetric(long value) {
        return value < 0 ? "unavailable" : Long.toString(value);
    }

    private record ProcessMetrics(int processCount, long rssKilobytes, int threadCount) {
        private static ProcessMetrics unavailable() {
            return new ProcessMetrics(-1, -1, -1);
        }
    }

    private record ProcessStats(long rssKilobytes, int threadCount) {
        private static ProcessStats unavailable() {
            return new ProcessStats(-1, -1);
        }

        private boolean hasKnownValue() {
            return rssKilobytes >= 0 || threadCount >= 0;
        }
    }
}
