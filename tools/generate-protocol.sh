#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
protoc_cmd=${PROTOC:-protoc}
[[ "$("$protoc_cmd" --version)" == 'libprotoc 33.4' ]] || {
  printf '%s\n' 'Protocol generation requires protoc 33.4 (Google.Protobuf runtime 3.33.4).' >&2
  exit 1
}
output="$repo/src/Ark.Client.Core/Protocol"
if [[ "${1:-}" == --check ]]; then
  output=$(mktemp -d "${TMPDIR:-/tmp}/ark-protobuf-generated.XXXXXX")
  trap 'rm -rf "$output"' EXIT
fi
"$protoc_cmd" --proto_path="$repo/protocol" --csharp_out="$output" "$repo/protocol/guest_room.proto"
if [[ "${1:-}" == --check ]]; then
  cmp "$output/GuestRoom.cs" "$repo/src/Ark.Client.Core/Protocol/GuestRoom.cs"
fi
