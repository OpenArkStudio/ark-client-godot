#!/usr/bin/env bash
set -euo pipefail
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet_cmd=${DOTNET:-dotnet}
"$dotnet_cmd" build "$repo/tests/Ark.Client.Core.Tests/Ark.Client.Core.Tests.csproj" --disable-build-servers -m:1
"$dotnet_cmd" "$repo/tests/Ark.Client.Core.Tests/bin/Debug/net8.0/Ark.Client.Core.Tests.dll"
