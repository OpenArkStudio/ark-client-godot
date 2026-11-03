#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
godot_cmd=${GODOT:-godot}
output=${1:-}
if [[ $# != 1 || "$output" != /*.app ]]; then
  printf 'Usage: %s /absolute/path/ArkGuestRoom.app\n' "$0" >&2
  exit 2
fi
if [[ $(uname -s) != Darwin ]]; then
  printf 'The development export must run on macOS.\n' >&2
  exit 2
fi
if [[ -e "$output" || -L "$output" ]]; then
  printf 'Output already exists: %s\n' "$output" >&2
  exit 2
fi
mkdir -p -- "$(dirname -- "$output")"
log=$(mktemp "${TMPDIR:-/tmp}/ark-godot-export.XXXXXX")
"$godot_cmd" --headless --editor --path "$repo" \
  --export-debug "macOS development" "$output" >"$log" 2>&1 || {
  cat "$log"
  printf 'Export failed; log: %s\n' "$log" >&2
  exit 1
}
cat "$log"
# Godot may return zero after its .NET publish plugin reports an error.
if grep -Eq '^(ERROR|SCRIPT ERROR):' "$log"; then
  printf 'Export reported an error; log: %s\n' "$log" >&2
  exit 1
fi
executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$output/Contents/Info.plist")
[[ -x "$output/Contents/MacOS/$executable" ]]
for architecture in arm64 x86_64; do
  [[ -s "$output/Contents/Resources/data_Ark.Client.Godot_macos_$architecture/Ark.Client.Godot.dll" ]]
done
printf 'Local macOS development app: %s\n' "$output"
