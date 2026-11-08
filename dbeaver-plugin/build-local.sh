#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
java_executable="${DBEAVER_JAVA:-$dbeaver_home/jre/Contents/Home/bin/java}"
dependency_dir="${SQLDEPS_DBEAVER_DEPS_DIR:-${TMPDIR:-/tmp}/sqldeps-lsp4j}"
maven_base_url="${SQLDEPS_DBEAVER_MAVEN_BASE_URL:-https://repo1.maven.org/maven2}"
build_root="${SQLDEPS_DBEAVER_BUILD_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-build}"
source_root="$repo_root/dbeaver-plugin/src"
classes_root="$build_root/classes"
staging_root="$build_root/staging"
bundle_path="$build_root/org.sqldeps.dbeaver_0.1.0.jar"
ecj_path="$dependency_dir/ecj-3.40.0.jar"
lsp4j_path="$dependency_dir/org.eclipse.lsp4j-0.24.0.jar"
jsonrpc_path="$dependency_dir/org.eclipse.lsp4j.jsonrpc-0.24.0.jar"

download_dependency() {
    local target_path="$1"
    local source_url="$2"

    if [[ -f "$target_path" ]]; then
        return
    fi

    mkdir -p "$(dirname "$target_path")"
    printf 'Downloading %s\n' "$(basename "$target_path")" >&2
    if ! curl --fail --location --silent --show-error "$source_url" --output "$target_path"; then
        rm -f "$target_path"
        printf 'Could not download %s.\n' "$(basename "$target_path")" >&2
        printf 'Pre-populate %s or set SQLDEPS_DBEAVER_MAVEN_BASE_URL to a reachable Maven mirror.\n' "$dependency_dir" >&2
        exit 2
    fi
}

if [[ ! -x "$java_executable" ]]; then
    printf 'DBeaver Java bulunamadı: %s\n' "$java_executable" >&2
    printf 'DBEAVER_JAVA veya DBEAVER_HOME değişkenlerini ayarlayın.\n' >&2
    exit 2
fi

if [[ ! -d "$dbeaver_home/plugins" ]]; then
    printf 'DBeaver plugin dizini bulunamadı: %s\n' "$dbeaver_home/plugins" >&2
    exit 2
fi

download_dependency \
    "$ecj_path" \
    "$maven_base_url/org/eclipse/jdt/ecj/3.40.0/ecj-3.40.0.jar"
download_dependency \
    "$lsp4j_path" \
    "$maven_base_url/org/eclipse/lsp4j/org.eclipse.lsp4j/0.24.0/org.eclipse.lsp4j-0.24.0.jar"
download_dependency \
    "$jsonrpc_path" \
    "$maven_base_url/org/eclipse/lsp4j/org.eclipse.lsp4j.jsonrpc/0.24.0/org.eclipse.lsp4j.jsonrpc-0.24.0.jar"

rm -rf "$build_root"
mkdir -p "$classes_root" "$staging_root/META-INF"

plugin_classpath="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name '*.jar' -print | paste -sd: -):$lsp4j_path:$jsonrpc_path"
source_files=()
while IFS= read -r -d '' source_file; do
    source_files+=("$source_file")
done < <(find "$source_root" -type f -name '*.java' -print0)

if [[ "${#source_files[@]}" -eq 0 ]]; then
    printf 'Java kaynak dosyası bulunamadı: %s\n' "$source_root" >&2
    exit 2
fi

printf 'Compiling sqldeps DBeaver plugin\n' >&2
"$java_executable" -jar "$ecj_path" \
    -proc:none \
    -encoding UTF-8 \
    -source 21 \
    -target 21 \
    -cp "$plugin_classpath" \
    -d "$classes_root" \
    "${source_files[@]}"

cp "$repo_root/dbeaver-plugin/META-INF/MANIFEST.MF" "$staging_root/META-INF/"
cp "$repo_root/dbeaver-plugin/plugin.xml" "$staging_root/"
cp -R "$classes_root"/* "$staging_root/"

(cd "$staging_root" && zip -qr "$bundle_path" .)

printf '%s\n' "$bundle_path"
