#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
godot_cmd=${GODOT:-godot}
host=${1:-127.0.0.1}
port=${2:-19100}
logs=$(mktemp -d "${TMPDIR:-/tmp}/ark-godot-smoke.XXXXXX")
alice_pid= bob_pid=
cleanup() {
  if [[ -n "$alice_pid" ]]; then kill "$alice_pid" 2>/dev/null || true; fi
  if [[ -n "$bob_pid" ]]; then kill "$bob_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM
"$godot_cmd" --headless --path "$repo" -- --smoke --host="$host" --port="$port" --name=Alice --peer-name=Bob >"$logs/alice.log" 2>&1 &
alice_pid=$!
"$godot_cmd" --headless --path "$repo" -- --smoke --host="$host" --port="$port" --name=Bob --peer-name=Alice >"$logs/bob.log" 2>&1 &
bob_pid=$!
alice_rc=0; bob_rc=0
wait "$alice_pid" || alice_rc=$?
alice_pid=
wait "$bob_pid" || bob_rc=$?
bob_pid=
cat "$logs/alice.log" "$logs/bob.log"
printf 'Local smoke logs: %s\n' "$logs"
[[ "$alice_rc" == 0 && "$bob_rc" == 0 ]]
grep -q '^SMOKE PASS Alice ' "$logs/alice.log"
grep -q '^SMOKE PASS Bob ' "$logs/bob.log"
grep -q '^SMOKE PEER ATTRIBUTES Alice ' "$logs/alice.log"
grep -q '^SMOKE PEER ATTRIBUTES Bob ' "$logs/bob.log"
grep -q '^SMOKE ATTRIBUTES Alice ' "$logs/alice.log"
grep -q '^SMOKE ATTRIBUTES Bob ' "$logs/bob.log"
