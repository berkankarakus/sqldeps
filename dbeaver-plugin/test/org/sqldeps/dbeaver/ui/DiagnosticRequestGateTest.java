package org.sqldeps.dbeaver.ui;

import java.util.concurrent.CompletableFuture;

public final class DiagnosticRequestGateTest {
    private DiagnosticRequestGateTest() {
    }

    public static void main(String[] args) {
        beginningNewRequestCancelsPreviousRequest();
        trackingStaleRequestCancelsItImmediately();
        closingGateInvalidatesCurrentRequest();

        System.out.println("sqldeps: Java diagnostic request gate tests ok");
    }

    private static void beginningNewRequestCancelsPreviousRequest() {
        DiagnosticRequestGate gate = new DiagnosticRequestGate();
        CompletableFuture<String> firstRequest = new CompletableFuture<>();

        long firstSequence = gate.begin();
        gate.track(firstSequence, firstRequest);
        long secondSequence = gate.begin();

        check(firstRequest.isCancelled(), "previous request cancellation");
        check(!gate.isCurrent(firstSequence), "previous sequence invalidation");
        check(gate.isCurrent(secondSequence), "new sequence activation");
    }

    private static void trackingStaleRequestCancelsItImmediately() {
        DiagnosticRequestGate gate = new DiagnosticRequestGate();
        CompletableFuture<String> staleRequest = new CompletableFuture<>();

        long staleSequence = gate.begin();
        gate.begin();
        gate.track(staleSequence, staleRequest);

        check(staleRequest.isCancelled(), "stale request cancellation");
    }

    private static void closingGateInvalidatesCurrentRequest() {
        DiagnosticRequestGate gate = new DiagnosticRequestGate();
        CompletableFuture<String> request = new CompletableFuture<>();
        long sequence = gate.begin();
        gate.track(sequence, request);

        gate.close();

        check(request.isCancelled(), "close request cancellation");
        check(!gate.isCurrent(sequence), "close sequence invalidation");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
