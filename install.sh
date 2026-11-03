#!/usr/bin/env bash
# Publishes sqldeps to ~/.local/share/sqldeps/app and links it as ~/.local/bin/sqldeps.
set -euo pipefail
cd "$(dirname "$0")"

app_dir="$HOME/.local/share/sqldeps/app"
publish_options=(-c Release -nologo -o "$app_dir")
if [[ "${SQLDEPS_NO_RESTORE:-}" == "1" ]]; then
  publish_options+=(--no-restore)
fi
dotnet publish "${publish_options[@]}" >/dev/null
mkdir -p "$HOME/.local/bin"
ln -sfn "$app_dir/sqldeps" "$HOME/.local/bin/sqldeps"
echo "Installed: $HOME/.local/bin/sqldeps"
