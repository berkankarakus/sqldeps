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
package org.sqldeps.dbeaver.editor.diagnostics;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.HashMap;
import java.util.Map;

import org.eclipse.core.resources.IMarker;
import org.jkiss.dbeaver.ui.editors.sql.syntax.SQLProblemAnnotation;

/** Adapts one sqldeps diagnostic to DBeaver's SQL problem annotation model. */
public final class SqldepsProblemAnnotation extends SQLProblemAnnotation {
    private final String type;
    private final String text;

    SqldepsProblemAnnotation(String type, String text) {
        super(createAnnotationMarker(text));
        this.type = type;
        this.text = text;
    }

    @Override
    public String getType() {
        return type;
    }

    @Override
    public String getText() {
        return text;
    }

    private static IMarker createAnnotationMarker(String text) {
        InvocationHandler handler = new AnnotationMarkerHandler(text);
        return (IMarker) Proxy.newProxyInstance(
            IMarker.class.getClassLoader(),
            new Class<?>[] { IMarker.class },
            handler
        );
    }

    private static final class AnnotationMarkerHandler implements InvocationHandler {
        private final Map<String, Object> attributes = new HashMap<>();

        private AnnotationMarkerHandler(String text) {
            attributes.put(IMarker.MESSAGE, text);
            attributes.put(IMarker.SEVERITY, IMarker.SEVERITY_WARNING);
            attributes.put(IMarker.TRANSIENT, true);
        }

        @Override
        public Object invoke(Object proxy, Method method, Object[] arguments) {
            String methodName = method.getName();
            if (methodName.equals("equals")) {
                return proxy == arguments[0];
            }

            if (methodName.equals("hashCode")) {
                return System.identityHashCode(proxy);
            }

            if (methodName.equals("toString")) {
                return "sqldeps annotation marker";
            }

            if (methodName.equals("exists")) {
                return true;
            }

            if (methodName.equals("getType")) {
                return SQLProblemAnnotation.MARKER_TYPE;
            }

            if (methodName.equals("getAttribute") && arguments != null && arguments.length == 2) {
                return attributes.getOrDefault(arguments[0], arguments[1]);
            }

            if (methodName.equals("getAttribute") && arguments != null && arguments.length == 1) {
                return attributes.get(arguments[0]);
            }

            if (methodName.equals("getAttributes")) {
                return Map.copyOf(attributes);
            }

            Class<?> returnType = method.getReturnType();
            if (returnType == boolean.class) {
                return false;
            }

            if (returnType == int.class) {
                return 0;
            }

            if (returnType == long.class) {
                return 0L;
            }

            if (returnType == float.class) {
                return 0.0F;
            }

            if (returnType == double.class) {
                return 0.0D;
            }

            if (returnType == short.class) {
                return (short) 0;
            }

            if (returnType == byte.class) {
                return (byte) 0;
            }

            if (returnType == char.class) {
                return (char) 0;
            }

            return null;
        }
    }
}
