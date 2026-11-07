#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
dependency_dir="${SQLDEPS_DBEAVER_DEPS_DIR:-${TMPDIR:-/tmp}/sqldeps-lsp4j}"
build_root="${SQLDEPS_DBEAVER_BUILD_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-build}"
publisher_source="$build_root/p2-source"
repository_root="${SQLDEPS_DBEAVER_UPDATE_SITE_DIR:-$build_root/p2-repository}"
configuration_root="${SQLDEPS_DBEAVER_P2_CONFIGURATION_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-p2-configuration}"
launcher_path="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name 'org.eclipse.equinox.launcher_*.jar' -print -quit)"

if [[ ! -d "$dbeaver_home" ]]; then
    printf 'DBeaver kurulumu bulunamadı: %s\n' "$dbeaver_home" >&2
    exit 2
fi

if [[ -z "$launcher_path" ]]; then
    printf 'Eclipse launcher bulunamadı: %s/plugins\n' "$dbeaver_home" >&2
    exit 2
fi

bundle_path="$(
    SQLDEPS_DBEAVER_BUILD_DIR="$build_root" \
        "$repo_root/dbeaver-plugin/build-local.sh"
)"
lsp4j_path="$dependency_dir/org.eclipse.lsp4j-0.24.0.jar"
jsonrpc_path="$dependency_dir/org.eclipse.lsp4j.jsonrpc-0.24.0.jar"
feature_root="$publisher_source/features/org.sqldeps.dbeaver.feature"

rm -rf "$publisher_source" "$repository_root"
mkdir -p "$publisher_source/plugins" "$feature_root" "$configuration_root"
cp "$bundle_path" "$publisher_source/plugins/"
cp "$lsp4j_path" "$publisher_source/plugins/"
cp "$jsonrpc_path" "$publisher_source/plugins/"
cp "$repo_root/dbeaver-plugin/feature/feature.xml" "$feature_root/"
cp "$repo_root/dbeaver-plugin/feature/feature.properties" "$feature_root/"

printf 'Publishing sqldeps DBeaver update site\n' >&2
"$dbeaver_home/jre/Contents/Home/bin/java" \
    -jar "$launcher_path" \
    -configuration "$configuration_root" \
    -application org.eclipse.equinox.p2.publisher.FeaturesAndBundlesPublisher \
    -metadataRepository "file:$repository_root" \
    -artifactRepository "file:$repository_root" \
    -source "$publisher_source" \
    -publishArtifacts \
    -compress

cp "$repo_root/dbeaver-plugin/site/site.xml" "$repository_root/"
printf '%s\n' "$repository_root"
