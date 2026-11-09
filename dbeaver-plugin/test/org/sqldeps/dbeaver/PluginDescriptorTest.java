package org.sqldeps.dbeaver;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

import javax.xml.parsers.DocumentBuilderFactory;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;

/**
 * Pins the plugin.xml facts that were verified in DBeaver for the SQL editor and the nested Source tab.
 *
 * <p>Run with -Dsqldeps.repo.root=&lt;repository root&gt;. See "Editor surfaces" in dbeaver-plugin/README.md.
 */
public final class PluginDescriptorTest {
    private static final String COMMAND_PREFIX = "org.sqldeps.dbeaver.commands.";
    private static final String EDITOR_POPUP = "popup:org.jkiss.dbeaver.ui.editors.sql.SQLEditor.EditorContext";
    private static final String SIDE_TOOLBAR = "toolbar:org.jkiss.dbeaver.ui.editors.sql.toolbar.side.top";
    private static final String JDT_QUICK_FIX = "org.eclipse.jdt.ui.edit.text.java.correction.assist.proposals";
    private static final Pattern COMMAND_LITERAL = Pattern.compile("\"(" + Pattern.quote(COMMAND_PREFIX) + "[A-Za-z]+)\"");

    private PluginDescriptorTest() {
    }

    public static void main(String[] args) throws Exception {
        Path root = Path.of(System.getProperty("sqldeps.repo.root", "."));
        Path plugin = root.resolve("dbeaver-plugin");
        Document descriptor = DocumentBuilderFactory.newInstance()
            .newDocumentBuilder()
            .parse(plugin.resolve("plugin.xml").toFile());

        Set<String> definedCommands = definedCommands(descriptor);
        List<Element> handlers = elements(descriptor, "extension", "org.eclipse.ui.handlers", "handler");

        handlersAreNotScopedWithActiveWhen(handlers);
        everyHandlerHasACommandAndASource(handlers, definedCommands, plugin);
        everyMenuCommandHasAHandler(descriptor, definedCommands, handlers);
        nestedContextMenuCommandsAreHandled(plugin, definedCommands, handlers);
        menuLocationsMatchTheVerifiedDbeaverIds(descriptor);
        toolbarCommandsUseIcons(descriptor);
        formatIsOfferedInTheTopMenuAndThePopup(descriptor);
        indexAndSuppressionCommandsAreAvailableEverywhere(handlers);

        System.out.println("sqldeps: DBeaver plugin descriptor tests ok");
    }

    /**
     * Verified: in the nested Source tab an activeWhen scope was not satisfied, so Quick Fix failed with
     * "Can't execute command" and index selection did nothing.
     */
    private static void handlersAreNotScopedWithActiveWhen(List<Element> handlers) {
        for (Element handler : handlers) {
            check(
                handler.getElementsByTagName("activeWhen").getLength() == 0,
                "handler " + handler.getAttribute("commandId") + " must not use activeWhen"
            );
            check(
                handler.getElementsByTagName("enabledWhen").getLength() == 0,
                "handler " + handler.getAttribute("commandId") + " must not use enabledWhen"
            );
        }
    }

    private static void everyHandlerHasACommandAndASource(
        List<Element> handlers,
        Set<String> definedCommands,
        Path plugin
    ) {
        Set<String> handledCommands = commandIds(handlers);

        check(handledCommands.contains(JDT_QUICK_FIX), "hover quick fix command id is handled");
        check(handledCommands.contains(COMMAND_PREFIX + "quickFixes"), "quick fix command is handled");

        for (Element handler : handlers) {
            String commandId = handler.getAttribute("commandId");
            String className = handler.getAttribute("class");
            Path source = plugin.resolve("src").resolve(className.replace('.', '/') + ".java");

            check(Files.isRegularFile(source), "handler class has a source file: " + className);
            check(
                commandId.equals(JDT_QUICK_FIX) || definedCommands.contains(commandId),
                "handled command is defined: " + commandId
            );
        }
    }

    private static void everyMenuCommandHasAHandler(
        Document descriptor,
        Set<String> definedCommands,
        List<Element> handlers
    ) {
        Set<String> handledCommands = commandIds(handlers);

        for (Element command : elements(descriptor, "extension", "org.eclipse.ui.menus", "command")) {
            String commandId = command.getAttribute("commandId");

            check(definedCommands.contains(commandId), "menu command is defined: " + commandId);
            check(handledCommands.contains(commandId), "menu command has a handler: " + commandId);
        }
    }

    /** Verified: the nested Source tab has its own context menu, filled from code (see NestedEditorContextMenu). */
    private static void nestedContextMenuCommandsAreHandled(
        Path plugin,
        Set<String> definedCommands,
        List<Element> handlers
    ) throws Exception {
        String source = Files.readString(
            plugin.resolve("src/org/sqldeps/dbeaver/editor/NestedEditorContextMenu.java")
        );
        Set<String> handledCommands = commandIds(handlers);
        Matcher matcher = COMMAND_LITERAL.matcher(source);
        int found = 0;

        while (matcher.find()) {
            found++;
            check(definedCommands.contains(matcher.group(1)), "nested menu command is defined: " + matcher.group(1));
            check(handledCommands.contains(matcher.group(1)), "nested menu command has a handler: " + matcher.group(1));
        }

        check(found >= 5, "nested editor context menu lists the sqldeps commands");
        check(source.contains(COMMAND_PREFIX + "format"), "nested editor context menu offers formatting");
        check(source.contains("addContextMenuContributor"), "nested menu uses DBeaver's context menu contributor hook");
    }

    /**
     * Verified: the regular SQL editor menu id works; popup.any does not reach it. The nested editor receives
     * no plugin.xml popup contribution, and its toolbar buttons are intentionally absent.
     */
    private static void menuLocationsMatchTheVerifiedDbeaverIds(Document descriptor) {
        List<String> locations = new ArrayList<>();

        for (Element contribution : elements(descriptor, "extension", "org.eclipse.ui.menus", "menuContribution")) {
            locations.add(contribution.getAttribute("locationURI"));
        }

        check(locations.stream().anyMatch(item -> item.startsWith(EDITOR_POPUP)), "regular editor popup location");
        check(locations.stream().anyMatch(item -> item.startsWith(SIDE_TOOLBAR)), "regular editor side toolbar");
        check(locations.stream().noneMatch(item -> item.startsWith("popup:org.eclipse.ui.popup.any")), "no popup.any");
        check(locations.stream().noneMatch(item -> item.startsWith("toolbar:dbeaver-general")), "no main toolbar buttons");
        check(locations.stream().noneMatch(item -> item.startsWith("popup:#SQLEditorContext")), "no nested popup id");
    }

    /** The format command must be reachable from the top menu and from the regular editor popup. */
    private static void formatIsOfferedInTheTopMenuAndThePopup(Document descriptor) {
        int entries = 0;

        for (Element command : elements(descriptor, "extension", "org.eclipse.ui.menus", "command")) {
            if ((COMMAND_PREFIX + "format").equals(command.getAttribute("commandId"))) {
                entries++;
            }
        }

        check(entries >= 2, "format appears in the top menu and in the editor popup");
    }

    private static void toolbarCommandsUseIcons(Document descriptor) {
        for (Element contribution : elements(descriptor, "extension", "org.eclipse.ui.menus", "menuContribution")) {
            if (!contribution.getAttribute("locationURI").startsWith("toolbar:")) {
                continue;
            }

            NodeList commands = contribution.getElementsByTagName("command");

            for (int index = 0; index < commands.getLength(); index++) {
                Element command = (Element) commands.item(index);

                check(
                    command.getAttribute("icon").startsWith("platform:/plugin/"),
                    "toolbar command uses an icon: " + command.getAttribute("commandId")
                );
            }
        }
    }

    /** Verified: these must work from the Source tab, where the active editor is not the SQL editor itself. */
    private static void indexAndSuppressionCommandsAreAvailableEverywhere(List<Element> handlers) {
        Set<String> handledCommands = commandIds(handlers);

        for (String name : List.of("selectIndex", "automaticIndex", "reindex", "manageSuppressions", "impact", "codeActions", "format")) {
            check(handledCommands.contains(COMMAND_PREFIX + name), "command is handled: " + name);
        }
    }

    private static Set<String> definedCommands(Document descriptor) {
        Set<String> ids = new HashSet<>();

        for (Element command : elements(descriptor, "extension", "org.eclipse.ui.commands", "command")) {
            ids.add(command.getAttribute("id"));
        }

        return ids;
    }

    private static Set<String> commandIds(List<Element> handlers) {
        Set<String> ids = new HashSet<>();

        for (Element handler : handlers) {
            ids.add(handler.getAttribute("commandId"));
        }

        return ids;
    }

    private static List<Element> elements(Document descriptor, String parentTag, String extensionPoint, String tag) {
        List<Element> result = new ArrayList<>();
        NodeList parents = descriptor.getElementsByTagName(parentTag);

        for (int index = 0; index < parents.getLength(); index++) {
            Element parent = (Element) parents.item(index);

            if (!extensionPoint.equals(parent.getAttribute("point"))) {
                continue;
            }

            NodeList children = parent.getElementsByTagName(tag);

            for (int childIndex = 0; childIndex < children.getLength(); childIndex++) {
                result.add((Element) children.item(childIndex));
            }
        }

        return result;
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Failed: " + description);
        }
    }
}
