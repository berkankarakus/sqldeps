#!/usr/bin/env bash
# Runs the indexed-data, CLI, and LSP smoke suites.
set -euo pipefail
cd "$(dirname "$0")"

database="$(mktemp -d)/deps.sqlite"
dotnet build -c Release -nologo -v q >/dev/null
dotnet run -c Release --no-build -- index fixtures --db "$database" >/dev/null

source tests/smoke/common.sh
source tests/smoke/index.sh
source tests/smoke/cli.sh
source tests/smoke/lsp.sh

echo "sqldeps: ok"
