#!/usr/bin/env bash
# Installs sqldeps components. Usage: ./install.sh [cli|dbeaver|all]   (default: cli)
#   cli      publishes sqldeps to ~/.local/share/sqldeps/app and links it as ~/.local/bin/sqldeps
#   dbeaver  installs the DBeaver plugin (delegates to dbeaver-plugin/install-local.sh)
#   all      cli, then dbeaver
set -euo pipefail
cd "$(dirname "$0")"

target="${1:-cli}"
case "$target" in
  cli|dbeaver|all) ;;
  *)
    echo "Usage: $0 [cli|dbeaver|all]" >&2
    exit 2
    ;;
esac

install_dbeaver() {
  # Prefer an explicit DBEAVER_HOME, then the user-writable copy, then the system install.
  local user_copy="${HOME:-}/Applications/DBeaver-sqldeps.app/Contents/Eclipse"
  if [[ -z "${DBEAVER_HOME:-}" && -d "$user_copy" ]]; then
    export DBEAVER_HOME="$user_copy"
  fi

  if pgrep -f "DBeaver.*\.app/Contents/MacOS/dbeaver" >/dev/null 2>&1; then
    echo "DBeaver is running. Close it completely before installing the plugin." >&2
    exit 2
  fi

  echo "Installing the DBeaver plugin into ${DBEAVER_HOME:-/Applications/DBeaver.app/Contents/Eclipse}"
  ./dbeaver-plugin/install-local.sh
}

if [[ "$target" == "dbeaver" ]]; then
  install_dbeaver
  exit 0
fi

install_home="${HOME:-${DOTNET_CLI_HOME:-}}"
if [[ -z "$install_home" ]]; then
  echo "Cannot determine an install home. Set HOME or DOTNET_CLI_HOME." >&2
  exit 2
fi

install_root="${SQLDEPS_INSTALL_ROOT:-$install_home/.local/share/sqldeps}"
app_dir="${SQLDEPS_APP_DIR:-$install_root/app}"
bin_dir="${SQLDEPS_BIN_DIR:-$install_home/.local/bin}"
publish_options=(-c Release -nologo -o "$app_dir")
# Stamp the build commit so `sqldeps --version` shows which source is installed.
if revision="$(git rev-parse --short HEAD 2>/dev/null)"; then
  if [[ -n "$(git status --porcelain 2>/dev/null)" ]]; then
    revision="$revision-dirty"
  fi
  publish_options+=("-p:SourceRevisionId=$revision")
fi
if [[ "${SQLDEPS_NO_RESTORE:-}" == "1" ]]; then
  publish_options+=(--no-restore)
fi
dotnet publish "${publish_options[@]}"
mkdir -p "$bin_dir"
ln -sfn "$app_dir/sqldeps" "$bin_dir/sqldeps"
echo "Installed: $bin_dir/sqldeps ($("$bin_dir/sqldeps" --version))"

if [[ "$target" == "all" ]]; then
  install_dbeaver
fi
