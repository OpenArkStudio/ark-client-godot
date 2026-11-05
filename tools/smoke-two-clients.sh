#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
godot_cmd=${GODOT:-godot}
client_cmd=${EXPORTED_CLIENT:-$godot_cmd}
if [[ -n "${EXPORTED_CLIENT:-}" ]]; then
  if [[ "$EXPORTED_CLIENT" != /* || ! -x "$EXPORTED_CLIENT" ]]; then
    printf 'EXPORTED_CLIENT must name an absolute executable path.\n' >&2
    exit 2
  fi
fi
host=${1:-127.0.0.1}
port=${2:-19100}
mode=${3:-}
if [[ -n "$mode" && "$mode" != --render ]]; then
  printf 'Usage: %s [host] [port] [--render]\n' "$0" >&2
  exit 2
fi
logs=$(mktemp -d "${TMPDIR:-/tmp}/ark-godot-smoke.XXXXXX")
engine_args=(--audio-driver Dummy)
if [[ -z "${EXPORTED_CLIENT:-}" ]]; then
  engine_args+=(--path "$repo")
fi
alice_args=(--smoke --host="$host" --port="$port" --name=Alice --peer-name=Bob)
bob_args=(--smoke --host="$host" --port="$port" --name=Bob --peer-name=Alice)
if [[ "$mode" == --render ]]; then
  alice_args+=(--capture-path="$logs/alice.png")
  bob_args+=(--capture-path="$logs/bob.png")
else
  engine_args+=(--headless)
fi
alice_pid= bob_pid=
cleanup() {
  if [[ -n "$alice_pid" ]]; then kill "$alice_pid" 2>/dev/null || true; fi
  if [[ -n "$bob_pid" ]]; then kill "$bob_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM
"$client_cmd" "${engine_args[@]}" -- "${alice_args[@]}" >"$logs/alice.log" 2>&1 &
alice_pid=$!
"$client_cmd" "${engine_args[@]}" -- "${bob_args[@]}" >"$logs/bob.log" 2>&1 &
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
grep -q '^SMOKE NESTED Alice ' "$logs/alice.log"
grep -q '^SMOKE NESTED Bob ' "$logs/bob.log"
if [[ "$mode" == --render ]]; then
  grep -q '^SMOKE RENDER Alice ' "$logs/alice.log"
  grep -q '^SMOKE RENDER Bob ' "$logs/bob.log"
  [[ -s "$logs/alice.png" && -s "$logs/bob.png" ]]
fi
