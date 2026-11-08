#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dbeaver_home="${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
dependency_dir="${SQLDEPS_DBEAVER_DEPS_DIR:-${TMPDIR:-/tmp}/sqldeps-lsp4j}"
build_root="${SQLDEPS_DBEAVER_BUILD_DIR:-${TMPDIR:-/tmp}/sqldeps-dbeaver-build}"
publisher_source="$build_root/p2-source"
repository_root="$build_root/p2-repository"
p2_configuration="${SQLDEPS_DBEAVER_P2_CONFIGURATION:-$build_root/p2-configuration}"
launcher_path="$(find "$dbeaver_home/plugins" -maxdepth 1 -type f -name 'org.eclipse.equinox.launcher_*.jar' -print -quit)"

if [[ ! -d "$dbeaver_home" ]]; then
    printf 'DBeaver kurulumu bulunamadı: %s\n' "$dbeaver_home" >&2
    exit 2
fi

if [[ ! -w "$dbeaver_home/plugins" || ! -w "$dbeaver_home/configuration" ]]; then
    printf 'DBeaver kurulumu yazılabilir değil: %s\n' "$dbeaver_home" >&2
    printf 'macOS /Applications altındaki uygulamalar varsayılan olarak korumalıdır.\n' >&2
    printf 'Önce uygulamayı kullanıcı dizinine kopyalayın:\n' >&2
    printf '  mkdir -p "$HOME/Applications"\n' >&2
    printf '  ditto /Applications/DBeaver.app "$HOME/Applications/DBeaver-sqldeps.app"\n' >&2
    printf 'Sonra kurulumu şu şekilde çalıştırın:\n' >&2
    printf '  DBEAVER_HOME="$HOME/Applications/DBeaver-sqldeps.app/Contents/Eclipse" %s\n' "$0" >&2
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

rm -rf "$publisher_source" "$repository_root"
mkdir -p "$publisher_source/plugins" "$p2_configuration"
cp "$bundle_path" "$publisher_source/plugins/"
cp "$lsp4j_path" "$publisher_source/plugins/"
cp "$jsonrpc_path" "$publisher_source/plugins/"

printf 'Publishing local p2 repository\n' >&2
"$dbeaver_home/jre/Contents/Home/bin/java" \
    -jar "$launcher_path" \
    -configuration "$p2_configuration" \
    -application org.eclipse.equinox.p2.publisher.FeaturesAndBundlesPublisher \
    -metadataRepository "file:$repository_root" \
    -artifactRepository "file:$repository_root" \
    -source "$publisher_source" \
    -publishArtifacts \
    -compress

printf 'Installing p2 IU into DBeaver\n' >&2
"$dbeaver_home/jre/Contents/Home/bin/java" \
    -jar "$launcher_path" \
    -configuration "$p2_configuration" \
    -application org.eclipse.equinox.p2.director \
    -repository "file:$repository_root" \
    -installIU org.sqldeps.dbeaver \
    -destination "$dbeaver_home" \
    -profile DefaultProfile \
    -bundlepool "$dbeaver_home" \
    -tag sqldeps-dbeaver-local

bundles_info="$dbeaver_home/configuration/org.eclipse.equinox.simpleconfigurator/bundles.info"
bundles_info_temporary="$(mktemp "${TMPDIR:-/tmp}/sqldeps-bundles.info.XXXXXX")"
cp "$lsp4j_path" "$dbeaver_home/plugins/org.eclipse.lsp4j_0.24.0.v20250131-1745.jar"
cp "$jsonrpc_path" "$dbeaver_home/plugins/org.eclipse.lsp4j.jsonrpc_0.24.0.v20250131-1745.jar"
cp "$bundle_path" "$dbeaver_home/plugins/org.sqldeps.dbeaver_0.1.0.qualifier.jar"

awk -F, -v OFS=, '
    $1 == "org.eclipse.lsp4j" {
        print "org.eclipse.lsp4j,0.24.0.v20250131-1745,plugins/org.eclipse.lsp4j_0.24.0.v20250131-1745.jar,4,false"
        found_lsp4j = 1
        next
    }

    $1 == "org.eclipse.lsp4j.jsonrpc" {
        print "org.eclipse.lsp4j.jsonrpc,0.24.0.v20250131-1745,plugins/org.eclipse.lsp4j.jsonrpc_0.24.0.v20250131-1745.jar,4,false"
        found_jsonrpc = 1
        next
    }

    $1 == "org.sqldeps.dbeaver" {
        print "org.sqldeps.dbeaver,0.1.0.qualifier,plugins/org.sqldeps.dbeaver_0.1.0.qualifier.jar,4,false"
        found_sqldeps = 1
        next
    }

    { print }

    END {
        if (!found_lsp4j) print "org.eclipse.lsp4j,0.24.0.v20250131-1745,plugins/org.eclipse.lsp4j_0.24.0.v20250131-1745.jar,4,false"
        if (!found_jsonrpc) print "org.eclipse.lsp4j.jsonrpc,0.24.0.v20250131-1745,plugins/org.eclipse.lsp4j.jsonrpc_0.24.0.v20250131-1745.jar,4,false"
        if (!found_sqldeps) print "org.sqldeps.dbeaver,0.1.0.qualifier,plugins/org.sqldeps.dbeaver_0.1.0.qualifier.jar,4,false"
    }
' "$bundles_info" > "$bundles_info_temporary"
mv "$bundles_info_temporary" "$bundles_info"

printf 'Installed sqldeps DBeaver plugin into the DBeaver p2 profile.\n'
printf 'Start DBeaver with -clean once after installation.\n'
