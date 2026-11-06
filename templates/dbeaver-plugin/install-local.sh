#!/usr/bin/env bash
set -euo pipefail

plugin_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
java_executable="${DBEAVER_JAVA:-}"
build_root="${DBEAVER_PLUGIN_BUILD_DIR:-${TMPDIR:-/tmp}/dbeaver-plugin-template-build}"
bundle_symbolic_name="${BUNDLE_SYMBOLIC_NAME:-org.example.dbeaver}"

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

if [[ ! -d "$dbeaver_home" ]]; then
    printf 'DBeaver kurulumu bulunamadı: %s\n' "$dbeaver_home" >&2
    exit 2
fi

launcher_path="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name 'org.eclipse.equinox.launcher_*.jar' -print -quit)"
if [[ -z "$launcher_path" ]]; then
    printf 'Eclipse launcher bulunamadı: %s/plugins\n' "$dbeaver_home" >&2
    exit 2
fi

bundle_path="$(
    DBEAVER_HOME="$dbeaver_home" \
        DBEAVER_JAVA="$java_executable" \
        DBEAVER_PLUGIN_BUILD_DIR="$build_root" \
        "$plugin_root/build-local.sh"
)"
repository_root="$build_root/p2-repository"
publisher_source="$build_root/p2-source"

rm -rf "$publisher_source" "$repository_root"
mkdir -p "$publisher_source/plugins"
cp "$bundle_path" "$publisher_source/plugins/"

"$java_executable" \
    -jar "$launcher_path" \
    -configuration "$dbeaver_home/configuration" \
    -application org.eclipse.equinox.p2.publisher.FeaturesAndBundlesPublisher \
    -metadataRepository "file:$repository_root" \
    -artifactRepository "file:$repository_root" \
    -source "$publisher_source" \
    -publishArtifacts \
    -compress

"$java_executable" \
    -jar "$launcher_path" \
    -configuration "$dbeaver_home/configuration" \
    -application org.eclipse.equinox.p2.director \
    -repository "file:$repository_root" \
    -installIU "$bundle_symbolic_name" \
    -destination "$dbeaver_home" \
    -profile DefaultProfile \
    -bundlepool "$dbeaver_home" \
    -tag "$bundle_symbolic_name-local"

printf 'Plugin %s DBeaver\'a kuruldu. -clean ile yeniden başlatmanız gerekebilir.\n' \
    "$bundle_symbolic_name"

