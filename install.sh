#!/usr/bin/env bash
# Publishes sqldeps to ~/.local/share/sqldeps/app and links it as ~/.local/bin/sqldeps.
set -euo pipefail
cd "$(dirname "$0")"

install_home="${HOME:-${DOTNET_CLI_HOME:-}}"
if [[ -z "$install_home" ]]; then
  echo "Cannot determine an install home. Set HOME or DOTNET_CLI_HOME." >&2
  exit 2
fi

install_root="${SQLDEPS_INSTALL_ROOT:-$install_home/.local/share/sqldeps}"
app_dir="${SQLDEPS_APP_DIR:-$install_root/app}"
bin_dir="${SQLDEPS_BIN_DIR:-$install_home/.local/bin}"
publish_options=(-c Release -nologo -o "$app_dir")
if [[ "${SQLDEPS_NO_RESTORE:-}" == "1" ]]; then
  publish_options+=(--no-restore)
fi
dotnet publish "${publish_options[@]}"
mkdir -p "$bin_dir"
ln -sfn "$app_dir/sqldeps" "$bin_dir/sqldeps"
echo "Installed: $bin_dir/sqldeps"
