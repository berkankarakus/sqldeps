package org.sqldeps.dbeaver.editor.index;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Locale;
import java.util.Set;

public final class RepositorySuppressionStoreTest {
    private RepositorySuppressionStoreTest() {
    }

    public static void main(String[] args) throws Exception {
        parsesAndWritesRepositoryRulesWithoutDroppingOtherSettings();
        removesRepositoryRulesWhenTheLastRuleIsEnabled();
        rejectsSymbolicLinkConfigurationFiles();
        acceptsSymbolicLinkRepositoryRoots();
        ruleNamesAreLowerCasedIndependentlyOfTheDefaultLocale();
        unavailableRootMessageNamesTheMissingStep();

        System.out.println("sqldeps: repository suppression tests ok");
    }

    private static void parsesAndWritesRepositoryRulesWithoutDroppingOtherSettings() throws Exception {
        Path repository = Files.createTempDirectory("sqldeps-suppressions-");

        try {
            Path configuration = repository.resolve(".sqldeps");
            Files.writeString(
                configuration,
                "default-schema=dbo\n"
                    + "# sqldeps: disable-repo=NOLOCK,select-star\n"
                    + "other-setting=true\n"
            );

            check(
                Set.of("nolock", "select-star").equals(RepositorySuppressionStore.read(repository)),
                "repository rules are normalized"
            );

            RepositorySuppressionStore.write(repository, Set.of("nolock", "merge"));
            String updated = Files.readString(configuration);
            check(updated.contains("default-schema=dbo"), "unrelated settings are preserved");
            check(updated.contains("other-setting=true"), "unrelated lines are preserved");
            check(updated.contains("sqldeps: disable-repo=merge,nolock"), "rules are rewritten");
            check(!updated.contains("select-star"), "removed rules are not retained");
        }

        finally {
            deleteDirectory(repository);
        }
    }

    private static void removesRepositoryRulesWhenTheLastRuleIsEnabled() throws Exception {
        Path repository = Files.createTempDirectory("sqldeps-suppressions-");

        try {
            Path configuration = repository.resolve(".sqldeps");
            Files.writeString(configuration, "sqldeps: disable-repo=nolock\n");

            RepositorySuppressionStore.write(repository, Set.of());

            check(Files.readString(configuration).isEmpty(), "last repository rule is removed");
        }

        finally {
            deleteDirectory(repository);
        }
    }

    private static void rejectsSymbolicLinkConfigurationFiles() throws Exception {
        Path repository = Files.createTempDirectory("sqldeps-suppressions-");
        Path target = Files.createTempFile("sqldeps-suppression-target-", ".txt");

        try {
            Files.createSymbolicLink(repository.resolve(".sqldeps"), target);

            boolean rejected = false;
            try {
                RepositorySuppressionStore.write(repository, Set.of("nolock"));
            }

            catch (java.io.IOException exception) {
                rejected = true;
            }

            check(rejected, "symbolic-link configuration is rejected");
        }

        finally {
            Files.deleteIfExists(repository.resolve(".sqldeps"));
            Files.deleteIfExists(target);
            Files.deleteIfExists(repository);
        }
    }

    private static void acceptsSymbolicLinkRepositoryRoots() throws Exception {
        Path target = Files.createTempDirectory("sqldeps-suppressions-target-");
        Path link = target.resolveSibling(target.getFileName() + "-link");

        try {
            Files.createSymbolicLink(link, target);
            RepositorySuppressionStore.write(link, Set.of("nolock"));

            check(
                Set.of("nolock").equals(RepositorySuppressionStore.read(target)),
                "symbolic-link repository roots resolve to their target"
            );
        }

        finally {
            Files.deleteIfExists(link);
            Files.deleteIfExists(target.resolve(".sqldeps"));
            Files.deleteIfExists(target);
        }
    }

    private static void ruleNamesAreLowerCasedIndependentlyOfTheDefaultLocale() {
        Locale previous = Locale.getDefault();

        try {
            Locale.setDefault(Locale.forLanguageTag("tr-TR"));

            check("isnull".equals(RepositorySuppressionStore.normalizeRule(" ISNULL ")), "dotless i is not produced");
            check(RepositorySuppressionStore.isValidRule("ISNULL"), "upper-case rule names stay valid");
        }

        finally {
            Locale.setDefault(previous);
        }
    }

    private static void unavailableRootMessageNamesTheMissingStep() {
        String withoutIndex = RepositorySuppressionStore.unavailableRootMessage(null);
        String withIndex = RepositorySuppressionStore.unavailableRootMessage("/tmp/example.sqlite");

        check(withoutIndex.contains("Select an sqldeps index"), "missing index asks for an index");
        check(withIndex.contains("active"), "selected index asks for an active editor session");
        check(!withIndex.contains("Select an sqldeps index"), "selected index is not asked to select again");
    }

    private static void deleteDirectory(Path directory) throws Exception {
        Files.deleteIfExists(directory.resolve(".sqldeps"));
        Files.deleteIfExists(directory);
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
