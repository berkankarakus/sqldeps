#!/usr/bin/env bash
# Publishes sqldeps to ~/.local/share/sqldeps/app and links it as ~/.local/bin/sqldeps.
set -euo pipefail
cd "$(dirname "$0")"

app_dir="$HOME/.local/share/sqldeps/app"
dotnet publish -c Release -nologo -o "$app_dir" >/dev/null
mkdir -p "$HOME/.local/bin"
ln -sfn "$app_dir/sqldeps" "$HOME/.local/bin/sqldeps"
echo "Installed: $HOME/.local/bin/sqldeps"
