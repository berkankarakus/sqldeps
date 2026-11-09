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
package org.sqldeps.dbeaver.editor.index;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.StandardOpenOption;
import java.util.Collections;
import java.util.Locale;
import java.util.Set;
import java.util.TreeSet;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Reads and updates repository-level sqldeps suppression directives. */
public final class RepositorySuppressionStore {
    private static final String FILE_NAME = ".sqldeps";
    private static final Pattern DIRECTIVE_PATTERN = Pattern.compile(
        "(?im)^[ \\t]*(?:#|--)?[ \\t]*sqldeps:[ \\t]*disable-repo[ \\t]*=[ \\t]*"
            + "(?<rules>[a-z0-9-]+(?:[ \\t]*,[ \\t]*[a-z0-9-]+)*)[ \\t]*(?:\\r\\n|\\r|\\n|$)"
    );
    private static final Pattern RULE_PATTERN = Pattern.compile("[a-z0-9-]+");

    private RepositorySuppressionStore() {
    }

    /** Returns the repository configuration file path. */
    public static Path configurationPath(Path repositoryRoot) {
        return repositoryRoot.toAbsolutePath().normalize().resolve(FILE_NAME);
    }

    /** Reads the currently suppressed repository rules, or an empty set when absent. */
    public static Set<String> read(Path repositoryRoot) throws IOException {
        Path configurationPath = checkedConfigurationPath(repositoryRoot);
        if (!Files.exists(configurationPath, LinkOption.NOFOLLOW_LINKS)) {
            return Set.of();
        }

        if (!Files.isRegularFile(configurationPath, LinkOption.NOFOLLOW_LINKS)) {
            throw new IOException("sqldeps configuration is not a regular file: " + configurationPath);
        }

        return readRules(Files.readString(configurationPath, StandardCharsets.UTF_8));
    }

    /** Parses repository suppression directives from configuration text. */
    public static Set<String> readRules(String content) {
        Set<String> rules = new TreeSet<>();
        Matcher matcher = DIRECTIVE_PATTERN.matcher(content);

        while (matcher.find()) {
            String[] values = matcher.group("rules").split(",");
            for (String value : values) {
                rules.add(normalizeRule(value));
            }
        }

        return Collections.unmodifiableSet(rules);
    }

    /** Updates repository directives while preserving unrelated configuration lines. */
    public static void write(Path repositoryRoot, Set<String> rules) throws IOException {
        Path configurationPath = checkedConfigurationPath(repositoryRoot);
        String current = "";

        if (Files.exists(configurationPath, LinkOption.NOFOLLOW_LINKS)) {
            if (!Files.isRegularFile(configurationPath, LinkOption.NOFOLLOW_LINKS)) {
                throw new IOException("sqldeps configuration is not a regular file: " + configurationPath);
            }

            current = Files.readString(configurationPath, StandardCharsets.UTF_8);
        }

        String withoutDirectives = DIRECTIVE_PATTERN.matcher(current).replaceAll("");
        String updated = appendDirective(withoutDirectives, rules);
        Path temporaryPath = Files.createTempFile(
            configurationPath.getParent(),
            ".sqldeps-",
            ".tmp"
        );

        try {
            Files.writeString(
                temporaryPath,
                updated,
                StandardCharsets.UTF_8,
                StandardOpenOption.TRUNCATE_EXISTING,
                StandardOpenOption.WRITE
            );

            moveIntoPlace(temporaryPath, configurationPath);
        }

        finally {
            Files.deleteIfExists(temporaryPath);
        }
    }

    /** Returns whether a rule can be represented by the sqldeps suppression syntax. */
    public static boolean isValidRule(String rule) {
        return rule != null && RULE_PATTERN.matcher(normalizeRule(rule)).matches();
    }

    /** Trims and lower-cases a rule name independently of the default locale (Turkish dotless i). */
    public static String normalizeRule(String rule) {
        return rule.trim().toLowerCase(Locale.ROOT);
    }

    /** Explains which step is missing when no repository root could be resolved. */
    public static String unavailableRootMessage(String indexPath) {
        if (indexPath == null || indexPath.isBlank()) {
            return "Select an sqldeps index first (sqldeps > Select sqldeps Index...).";
        }

        return "sqldeps could not read the repository root of the selected index.\n\n"
            + "Open an SQL or source editor so that its sqldeps session is active, then try again. "
            + "If the problem remains, reinstall the CLI together with the plugin (./install.sh all).";
    }

    private static Path checkedConfigurationPath(Path repositoryRoot) throws IOException {
        if (repositoryRoot == null) {
            throw new IOException("sqldeps repository root is not available");
        }

        Path normalizedRoot = repositoryRoot.toAbsolutePath().normalize();
        if (!Files.isDirectory(normalizedRoot)) {
            throw new IOException("sqldeps repository root is not a directory: " + normalizedRoot);
        }

        Path configurationPath = normalizedRoot.toRealPath().resolve(FILE_NAME);
        if (Files.isSymbolicLink(configurationPath)) {
            throw new IOException("refusing to edit symbolic-link configuration: " + configurationPath);
        }

        return configurationPath;
    }

    private static void moveIntoPlace(Path temporaryPath, Path configurationPath) throws IOException {
        if (Files.isSymbolicLink(configurationPath)) {
            throw new IOException("refusing to edit symbolic-link configuration: " + configurationPath);
        }

        try {
            Files.move(
                temporaryPath,
                configurationPath,
                StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING
            );
        }

        catch (AtomicMoveNotSupportedException exception) {
            Files.move(temporaryPath, configurationPath, StandardCopyOption.REPLACE_EXISTING);
        }
    }

    private static String appendDirective(String content, Set<String> rules) {
        Set<String> normalizedRules = new TreeSet<>();
        for (String rule : rules) {
            if (!isValidRule(rule)) {
                throw new IllegalArgumentException("Invalid sqldeps suppression rule: " + rule);
            }

            normalizedRules.add(normalizeRule(rule));
        }

        if (normalizedRules.isEmpty()) {
            return content;
        }

        StringBuilder updated = new StringBuilder(content);
        if (updated.length() > 0 && updated.charAt(updated.length() - 1) != '\n'
            && updated.charAt(updated.length() - 1) != '\r') {
            updated.append('\n');
        }

        updated.append("sqldeps: disable-repo=");
        updated.append(String.join(",", normalizedRules));
        updated.append('\n');

        return updated.toString();
    }
}
