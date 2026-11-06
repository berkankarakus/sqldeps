#!/usr/bin/env bash
set -euo pipefail

plugin_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
java_executable="${DBEAVER_JAVA:-}"
dependency_dir="${DBEAVER_PLUGIN_DEPS_DIR:-${TMPDIR:-/tmp}/dbeaver-plugin-template-deps}"
build_root="${DBEAVER_PLUGIN_BUILD_DIR:-${TMPDIR:-/tmp}/dbeaver-plugin-template-build}"
bundle_symbolic_name="${BUNDLE_SYMBOLIC_NAME:-org.example.dbeaver}"
bundle_version="${BUNDLE_VERSION:-0.1.0}"
ecj_version="${ECJ_VERSION:-3.40.0}"
lsp4j_version="${LSP4J_VERSION:-0.24.0}"

if [[ -z "$java_executable" ]]; then
    if [[ -x "$dbeaver_home/jre/Contents/Home/bin/java" ]]; then
        java_executable="$dbeaver_home/jre/Contents/Home/bin/java"
    elif [[ -x "$dbeaver_home/jre/bin/java" ]]; then
        java_executable="$dbeaver_home/jre/bin/java"
    else
        java_executable="$(command -v java || true)"
    fi
fi

if [[ -z "$java_executable" || ! -x "$java_executable" ]]; then
    printf 'Java bulunamadı. DBEAVER_JAVA veya DBEAVER_HOME değişkenlerini ayarlayın.\n' >&2
    exit 2
fi

if [[ ! -d "$dbeaver_home/plugins" ]]; then
    printf 'DBeaver plugin dizini bulunamadı: %s\n' "$dbeaver_home/plugins" >&2
    exit 2
fi

download_dependency() {
    local target_path="$1"
    local source_url="$2"

    if [[ -f "$target_path" ]]; then
        return
    fi

    mkdir -p "$(dirname "$target_path")"
    printf 'Downloading %s\n' "$(basename "$target_path")" >&2
    curl --fail --location --silent --show-error "$source_url" --output "$target_path"
}

ecj_path="$dependency_dir/ecj-$ecj_version.jar"
lsp4j_path="$dependency_dir/org.eclipse.lsp4j-$lsp4j_version.jar"
jsonrpc_path="$dependency_dir/org.eclipse.lsp4j.jsonrpc-$lsp4j_version.jar"

download_dependency \
    "$ecj_path" \
    "https://repo1.maven.org/maven2/org/eclipse/jdt/ecj/$ecj_version/ecj-$ecj_version.jar"
download_dependency \
    "$lsp4j_path" \
    "https://repo1.maven.org/maven2/org/eclipse/lsp4j/org.eclipse.lsp4j/$lsp4j_version/org.eclipse.lsp4j-$lsp4j_version.jar"
download_dependency \
    "$jsonrpc_path" \
    "https://repo1.maven.org/maven2/org/eclipse/lsp4j/org.eclipse.lsp4j.jsonrpc/$lsp4j_version/org.eclipse.lsp4j.jsonrpc-$lsp4j_version.jar"

rm -rf "$build_root"
mkdir -p "$build_root/classes" "$build_root/staging/META-INF"

plugin_classpath="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name '*.jar' -print | paste -sd: -):$lsp4j_path:$jsonrpc_path"
source_files=()
while IFS= read -r -d '' source_file; do
    source_files+=("$source_file")
done < <(find "$plugin_root/src" -type f -name '*.java' -print0)

if [[ "${#source_files[@]}" -eq 0 ]]; then
    printf 'Java kaynak dosyası bulunamadı: %s\n' "$plugin_root/src" >&2
    exit 2
fi

"$java_executable" -jar "$ecj_path" \
    -proc:none \
    -encoding UTF-8 \
    -source 21 \
    -target 21 \
    -cp "$plugin_classpath" \
    -d "$build_root/classes" \
    "${source_files[@]}"

cp "$plugin_root/META-INF/MANIFEST.MF" "$build_root/staging/META-INF/"
cp "$plugin_root/plugin.xml" "$build_root/staging/"
cp -R "$build_root/classes"/* "$build_root/staging/"

bundle_path="$build_root/${bundle_symbolic_name}_${bundle_version}.jar"
(cd "$build_root/staging" && zip -qr "$bundle_path" .)

printf '%s\n' "$bundle_path"

