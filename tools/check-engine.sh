#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
godot_cmd=${GODOT:-godot}
logs=$(mktemp -d "${TMPDIR:-/tmp}/ark-godot-engine.XXXXXX")
"$godot_cmd" --headless --editor --path "$repo" --quit >"$logs/import.log" 2>&1 || {
  cat "$logs/import.log"
  exit 1
}
"$godot_cmd" --headless --path "$repo" --quit-after 10 >"$logs/run.log" 2>&1 || {
  cat "$logs/run.log"
  exit 1
}
cat "$logs/import.log" "$logs/run.log"
if grep -Eq '^(ERROR|SCRIPT ERROR):' "$logs/import.log" "$logs/run.log"; then
  exit 1
fi
grep -q '^CLIENT READY scene=GuestRoom language=C# protocol=ARK2$' "$logs/run.log"
