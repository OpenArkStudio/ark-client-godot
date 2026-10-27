#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
"$dotnet_cmd" build "$repo/Ark.Client.Godot.csproj" --disable-build-servers -m:1
