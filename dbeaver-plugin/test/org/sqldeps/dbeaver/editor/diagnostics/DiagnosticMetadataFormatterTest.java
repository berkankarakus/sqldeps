package org.sqldeps.dbeaver.editor.diagnostics;

import java.util.List;
import java.util.Map;

import org.eclipse.lsp4j.Diagnostic;

public final class DiagnosticMetadataFormatterTest {
    private DiagnosticMetadataFormatterTest() {
    }

    public static void main(String[] args) {
        formatsEvidenceAndReferences();
        ignoresMissingMetadata();

        System.out.println("sqldeps: DBeaver diagnostic metadata tests ok");
    }

    private static void formatsEvidenceAndReferences() {
        Diagnostic diagnostic = new Diagnostic();
        diagnostic.setData(Map.of(
            "source", "live-document",
            "confidence", "low",
            "stale", false,
            "evidence", Map.of(
                "summary", "NOLOCK may read dirty data.",
                "guidance", "Use a transactionally appropriate isolation level.",
                "references", List.of(Map.of(
                    "title", "Microsoft Learn: Table hints",
                    "url", "https://learn.microsoft.com/sql"
                ))
            )
        ));

        String result = DiagnosticMetadataFormatter.format(diagnostic);

        check(result.contains("Analysis: live-document"), "analysis source is visible");
        check(result.contains("Confidence: low"), "confidence is visible");
        check(result.contains("Status: current"), "freshness is visible");
        check(result.contains("Guidance: Use a transactionally appropriate isolation level."), "guidance is visible");
        check(result.contains("Reference: Microsoft Learn: Table hints (https://learn.microsoft.com/sql)"), "reference is visible");
    }

    private static void ignoresMissingMetadata() {
        Diagnostic diagnostic = new Diagnostic();

        check(DiagnosticMetadataFormatter.format(diagnostic).isEmpty(), "missing metadata stays empty");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
