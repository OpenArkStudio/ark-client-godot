#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
projects=()
if [[ "${1:-}" != --scene-only ]]; then
  projects+=(
    "$repo/src/Ark.Client.Core/Ark.Client.Core.csproj"
    "$repo/tests/Ark.Client.Core.Tests/Ark.Client.Core.Tests.csproj"
  )
fi
if [[ "${1:-}" != --core-only ]]; then
  projects+=("$repo/Ark.Client.Godot.csproj")
fi
for project in "${projects[@]}"; do
  "$dotnet_cmd" format whitespace "$project" --no-restore --verify-no-changes
  "$dotnet_cmd" format style "$project" --no-restore --verify-no-changes --diagnostics IDE0011 --severity info
done
