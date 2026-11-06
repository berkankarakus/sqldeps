fail() { echo "FAIL: $*" >&2; exit 1; }

smoke_work="$(mktemp -d)"
sqldeps_app="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/bin/Release/net10.0/sqldeps"

expect_on() {
  local file="$1" description="$2" sql="$3" expected="$4" actual
  actual="$(sqlite3 "$file" "$sql")"
  [[ "$actual" == "$expected" ]] || fail "$description (expected: $expected, got: $actual)"
}

expect() { expect_on "$database" "$@"; }

index() { "$sqldeps_app" index "$@"; }

lsp_session_input=""
lsp_session_output=""
lsp_session_pid=""
lsp_sync_id=2147483000

lsp_session_start() {
  local session_root
  session_root="$(mktemp -d "${TMPDIR:-/tmp}/sqldeps-lsp-session.XXXXXX")"
  lsp_session_input="$session_root/input"
  lsp_session_output="$session_root/output"
  mkfifo "$lsp_session_input" "$lsp_session_output"

  "$sqldeps_app" lsp <"$lsp_session_input" >"$lsp_session_output" &
  lsp_session_pid="$!"
  exec 7>"$lsp_session_input"
  exec 8<"$lsp_session_output"
}

lsp_session_stop() {
  if [[ -n "$lsp_session_pid" ]]; then
    exec 7>&- 8<&- || true
    kill "$lsp_session_pid" 2>/dev/null || true
    wait "$lsp_session_pid" 2>/dev/null || true
    lsp_session_pid=""
  fi
}

trap lsp_session_stop EXIT

lsp_initialize='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":null,"initializationOptions":{"experimentalSuggestions":true}}}'
lsp_default_initialize='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":null}}'

lsp_frame() {
  local message="$1"
  local byte_length
  byte_length="$(printf '%s' "$message" | wc -c | tr -d '[:space:]')"
  printf 'Content-Length: %s\r\n\r\n%s' "$byte_length" "$message"
}

lsp_frame_file() {
  local file="$1"
  local byte_length
  byte_length="$(wc -c < "$file" | tr -d '[:space:]')"
  printf 'Content-Length: %s\r\n\r\n' "$byte_length"
  cat "$file"
}

lsp_read_until_sync() {
  local sync_id="$1"
  local capture="$2"
  local header
  local separator
  local body
  local content_length
  local output=""

  while IFS= read -r header <&8; do
    header="${header%$'\r'}"

    if [[ "$header" != Content-Length:* ]]; then
      continue
    fi

    content_length="${header#Content-Length: }"
    IFS= read -r separator <&8
    body="$(dd bs=1 count="$content_length" <&8 2>/dev/null)"

    if [[ "$body" == *"\"id\":$sync_id"* ]]; then
      break
    fi

    if [[ "$capture" == true ]]; then
      output+="$body"$'\n'
    fi
  done

  printf '%s' "$output"
}

lsp_document_uri() {
  local message="$1"

  printf '%s' "$message" | sed -n 's/.*"uri":"\([^"]*\)".*/\1/p'
}

lsp_run() {
  local message
  local document_uri
  local existing_uri
  local known_uri
  local close_message
  local output=""
  local -a document_uris=()

  for message in "$@"; do
    lsp_frame "$message" >&7

    document_uri="$(lsp_document_uri "$message")"

    if [[ "$document_uri" != file://* ]]; then
      document_uri=""
    fi

    known_uri=false

    for existing_uri in "${document_uris[@]-}"; do
      if [[ "$existing_uri" == "$document_uri" ]]; then
        known_uri=true

        break
      fi
    done

    if [[ -n "$document_uri" && "$known_uri" == false ]]; then
      document_uris+=("$document_uri")
    fi
  done

  lsp_sync_id=$((lsp_sync_id + 1))
  local sync_message="{\"jsonrpc\":\"2.0\",\"id\":$lsp_sync_id,\"method\":\"\$/sqldeps/sync\",\"params\":{}}"
  lsp_frame "$sync_message" >&7
  output="$(lsp_read_until_sync "$lsp_sync_id" true)"

  if ((${#document_uris[@]} > 0)); then
    for document_uri in "${document_uris[@]}"; do
      close_message="{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didClose\",\"params\":{\"textDocument\":{\"uri\":\"$document_uri\"}}}"
      lsp_frame "$close_message" >&7
    done
  fi

  lsp_sync_id=$((lsp_sync_id + 1))
  sync_message="{\"jsonrpc\":\"2.0\",\"id\":$lsp_sync_id,\"method\":\"\$/sqldeps/sync\",\"params\":{}}"
  lsp_frame "$sync_message" >&7
  lsp_read_until_sync "$lsp_sync_id" false >/dev/null

  printf '%s' "$output"
}
